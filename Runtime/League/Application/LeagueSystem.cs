using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DreamTech.Leaderboard.League
{
    /// <summary>
    /// Mặt tiền của League cho game: báo thắng/thua/thoát, tải trang, khép mùa, nhận thưởng. Mọi phần có thể thay đều được
    /// cắm qua <see cref="LeagueSystemBuilder"/>; class này chỉ điều phối và giữ trạng thái trên máy.
    ///
    /// <para>Chỉ dùng trên main thread (giống <see cref="LeaderboardBoard"/>). Thắng level được ghi NGAY (đồng bộ, không chờ mạng)
    /// vào hàng đợi cúp chờ gửi; <see cref="FlushPendingTrophiesAsync"/> gửi sau, gửi lại bao nhiêu lần cũng chỉ cộng một lần.</para>
    ///
    /// <para><b>Không <c>ConfigureAwait(false)</c> trong League.</b> Sau mỗi await, class này đổi trạng thái, ghi
    /// <see cref="ILeagueTextStore"/> (PlayerPrefs chỉ gọi được trên main thread), bắn <see cref="StateChanged"/> cho UI và đọc
    /// <see cref="ILeagueClock"/> của host. Await phải giữ context của nơi gọi để phần đó quay về main thread kể cả khi backend hoàn tất
    /// Task trên thread pool. Bỏ context ở MỘT chỗ trong chuỗi gọi là đủ hỏng: context kiểu UnitySynchronizationContext không cho chạy
    /// ngay phần tiếp theo đã bỏ context nên .NET đẩy nó sang thread pool, và mọi lượt gọi bắt đầu từ đó (gửi cúp, hỏi bảng) mất main
    /// thread luôn.</para>
    /// </summary>
    public sealed class LeagueSystem
    {
        private const string TrophyGrantKind = "trophy";
        private const string SeasonRewardGrantPrefix = "season-reward-";
        private const string StreakRewardGrantPrefix = "streak-reward-";

        private readonly ILeagueTextStore _textStore;
        private readonly ILeagueRewardGranter _rewardGranter;
        private readonly ILeagueFeatureGate _featureGate;
        private readonly string _stateKey;
        private readonly LeagueLocalState _state;

        /// <summary>Lượt gửi hàng chờ cúp gần nhất; đã đóng thì người gọi sau mở lượt mới.</summary>
        private SharedOperationRun<int> _activeFlush;

        internal LeagueSystem(string systemId, LeagueRules rules, WinStreakLadder streakLadder, ILeagueGroupService groupService,
                              ISeasonSchedule schedule, ILeagueClock clock, ILeagueTextStore textStore, ILeagueRewardGranter rewardGranter,
                              ILeagueFeatureGate featureGate, IWinStreakRule streakRule, ITrophyRule trophyRule)
        {
            SystemId = systemId;
            Rules = rules;
            StreakLadder = streakLadder;
            GroupService = groupService;
            Schedule = schedule;
            Clock = clock;
            StreakRule = streakRule;
            TrophyRule = trophyRule;
            _textStore = textStore;
            _rewardGranter = rewardGranter;
            _featureGate = featureGate;
            _stateKey = "league." + systemId + ".local";
            _state = _textStore.TryRead(_stateKey, out string stored) ? LeagueLocalState.Decode(stored) : new LeagueLocalState();
        }

        public string SystemId { get; }
        public LeagueRules Rules { get; }
        public WinStreakLadder StreakLadder { get; }
        public ILeagueGroupService GroupService { get; }
        public ISeasonSchedule Schedule { get; }
        public ILeagueClock Clock { get; }
        public IWinStreakRule StreakRule { get; }
        public ITrophyRule TrophyRule { get; }

        public bool IsUnlocked => _featureGate.IsUnlocked;
        public SeasonWindow CurrentSeason => Schedule.GetSeasonAt(Clock.UtcNow);
        public TimeSpan TimeLeftInCurrentSeason => CurrentSeason.TimeLeft(Clock.UtcNow);

        public WinStreakState Streak => _state.Streak;
        public int StreakMultiplier => StreakLadder.MultiplierAt(_state.Streak.Level);

        public int PendingTrophyGrantCount => _state.PendingTrophyGrants.Count;
        public int PendingRewardCount => _state.PendingRewards.Count;

        /// <summary>Tổng cúp chờ gửi của MỌI mùa. Hiển thị điểm của một mùa thì dùng <see cref="GetUnsentTrophies"/>.</summary>
        public int UnsentTrophies
        {
            get
            {
                int total = 0;
                foreach (LeagueTrophyGrant grant in _state.PendingTrophyGrants) total += grant.Trophies;
                return total;
            }
        }

        /// <summary>
        /// Số grant dịch vụ nhóm đã từ chối dứt khoát trong phiên này (xem <see cref="TrophyGrantRejected"/>). Không lưu qua lần mở
        /// app — đây là số cho debug / cheat, sự kiện mới là kênh báo chính.
        /// </summary>
        public int RejectedTrophyGrantCount { get; private set; }

        /// <summary>Streak, cúp chờ gửi hoặc quà chờ phát vừa đổi.</summary>
        public event Action StateChanged;

        /// <summary>
        /// Dịch vụ nhóm từ chối dứt khoát một grant (<see cref="LeagueTrophyGrantRejectedException"/>): grant đã bị bỏ khỏi hàng chờ
        /// để không chặn các grant phía sau. Game ghi log / analytics ở đây — cúp không được biến mất im lặng.
        /// </summary>
        public event Action<LeagueTrophyGrant, LeagueTrophyGrantRejection> TrophyGrantRejected;

        /// <summary>
        /// Cúp chờ gửi của riêng mùa <paramref name="seasonId"/>. Trang của mùa mới không được cộng cúp còn nợ của mùa cũ vào điểm
        /// đang hiện (cúp đó sẽ vào kết quả mùa cũ).
        /// </summary>
        public int GetUnsentTrophies(string seasonId)
        {
            int total = 0;
            foreach (LeagueTrophyGrant grant in _state.PendingTrophyGrants)
            {
                if (string.Equals(grant.SeasonId, seasonId, StringComparison.Ordinal)) total += grant.Trophies;
            }
            return total;
        }

        // ---------------------------------------------------------------- Trong level

        /// <summary>Ghi một lần thắng: tính cúp theo streak, nâng streak, xếp cúp vào hàng chờ gửi. Không chờ mạng.</summary>
        public LevelWinOutcome RecordLevelWin(in LevelWinContext context)
        {
            WinStreakState before = _state.Streak;
            if (!IsUnlocked) return new LevelWinOutcome(false, 0, before, before, null);

            WinStreakState after = StreakRule.Apply(before, WinStreakEvent.LevelWon, StreakLadder);
            int trophies = TrophyRule.TrophiesForWin(context, before, after, StreakLadder);
            string grantId = null;
            if (trophies > 0)
            {
                grantId = _state.NextGrantId(TrophyGrantKind);
                // Grant mang cả cửa sổ mùa: gửi được lần đầu khi mùa đó đã bị nhảy qua thì dịch vụ vẫn dựng được sổ cho nó.
                _state.PendingTrophyGrants.Add(new LeagueTrophyGrant(grantId, CurrentSeason, trophies));
            }

            ApplyStreak(before, after);
            SaveAndNotify();
            GrantPendingRewards();
            return new LevelWinOutcome(true, trophies, before, after, grantId);
        }

        /// <summary>Báo thua hẳn / thoát / chơi lại / hồi sinh. Thắng phải đi qua <see cref="RecordLevelWin"/>.</summary>
        public WinStreakChange RecordStreakEvent(WinStreakEvent streakEvent)
        {
            if (streakEvent == WinStreakEvent.LevelWon)
            {
                throw new ArgumentException("Thắng level phải gọi RecordLevelWin để còn tính cúp.", nameof(streakEvent));
            }
            WinStreakState before = _state.Streak;
            if (!IsUnlocked) return new WinStreakChange(before, before);

            WinStreakState after = StreakRule.Apply(before, streakEvent, StreakLadder);
            if (!after.Equals(before))
            {
                ApplyStreak(before, after);
                SaveAndNotify();
            }
            return new WinStreakChange(before, after);
        }

        /// <summary>Popup thoát / thua hỏi hàm này để biết có cần cảnh báo mất streak không.</summary>
        public bool WouldLoseStreak(WinStreakEvent streakEvent)
        {
            return IsUnlocked && streakEvent != WinStreakEvent.LevelWon && StreakRule.WouldReset(_state.Streak, streakEvent);
        }

        // ---------------------------------------------------------------- Đồng bộ với dịch vụ nhóm

        /// <summary>
        /// Gửi các lần cộng cúp đang chờ, theo thứ tự. Gọi chồng nhau thì dùng chung một lượt gửi. Lỗi mạng ném ra ngoài; phần chưa
        /// gửi được vẫn nằm trong hàng chờ cho lần sau. Grant bị dịch vụ từ chối dứt khoát thì bỏ khỏi hàng chờ, báo qua
        /// <see cref="TrophyGrantRejected"/>, gửi tiếp grant sau. Trả số grant đã được dịch vụ nhận (không tính grant bị từ chối).
        ///
        /// <para><b>Huỷ chỉ tác động người gọi.</b> Lượt gửi dùng chung chạy bằng token riêng: <paramref name="cancellationToken"/> bị
        /// huỷ thì chỉ Task của người gọi này kết thúc ở trạng thái huỷ, lượt gửi vẫn chạy tiếp cho những người còn chờ. Chỉ khi MỌI
        /// người đang chờ đều đã huỷ (không còn ai cần kết quả) thì lượt gửi mới dừng — grant đang gửi dở còn nguyên trong hàng chờ,
        /// lần sau gửi lại (idempotent theo grant id). Trước đây token của người mở lượt gửi huỷ luôn lượt dùng chung: người gọi khác
        /// nhận huỷ không phải của mình, coi như lỗi mạng rồi hỏi bảng mùa mới khi cúp mùa cũ còn nằm hàng chờ.</para>
        /// </summary>
        public Task<int> FlushPendingTrophiesAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<int>(cancellationToken);

            SharedOperationRun<int> run = _activeFlush;
            if (run == null || !run.TryJoin())
            {
                run = new SharedOperationRun<int>();
                run.TryJoin();
                _activeFlush = run;
                _ = run.RunAsync(FlushCoreAsync);
            }
            return run.WaitAsync(cancellationToken);
        }

        /// <summary>Tải trang League. Gửi cúp chờ trước (lỗi thì bỏ qua, số chưa gửi nằm ở <see cref="LeaguePageData.UnsentTrophies"/>).</summary>
        public async Task<LeaguePageData> LoadPageAsync(CancellationToken cancellationToken)
        {
            await TryFlushAsync(cancellationToken);
            LeagueGroupSnapshot group = await GroupService.GetGroupAsync(CurrentSeason, cancellationToken);
            if (group == null) throw new InvalidOperationException("GetGroupAsync trả về null — dịch vụ nhóm phải ném lỗi thay vì trả null.");
            return new LeaguePageData(group, Rules, _state.Streak, StreakLadder, Clock.UtcNow, GetUnsentTrophies(group.Season.SeasonId));
        }

        /// <summary>
        /// Kết quả mùa cũ còn việc cho UI (popup kết quả / rương). Cúp chờ gửi được gửi trước để mùa cũ khép đúng điểm.
        ///
        /// <para>Gửi không được (mất mạng) mà hàng chờ còn cúp của mùa KHÁC mùa hiện tại thì trả null, không hỏi dịch vụ: hiện kết
        /// quả thiếu cúp rồi người chơi bấm xem xong thì cúp tới sau bị từ chối (kết quả đã chốt). Lần gọi sau gửi được thì kết quả
        /// hiện ra với đủ cúp.</para>
        ///
        /// <para>Kiểm lần nữa SAU khi dịch vụ trả kết quả: kết quả thuộc mùa còn cúp chờ gửi thì cũng trả null (lượt sau thử lại). Lượt
        /// gọi vắt qua mốc đổi mùa lọt qua lần kiểm đầu — lúc hỏi, cúp chờ gửi còn là của mùa hiện tại — nhưng tới khi dịch vụ xử lý thì
        /// một lượt gọi khác (vd mở trang League sau mốc) đã khép mùa đó thiếu cúp. Không kiểm lại thì kết quả thiếu cúp được hiện, xem
        /// xong là chốt, và các cúp đó bị từ chối <see cref="LeagueTrophyGrantRejection.SeasonAlreadyFinalized"/>.</para>
        ///
        /// <para>Kết quả thuộc đúng mùa đọc trước lượt gọi cũng trả null: mùa đó bị khép trong lúc gọi, và với backend nhiều kết nối
        /// thì cúp muộn của mùa có thể đã được gửi (hàng chờ trống) trong khi phản hồi mang bản tính TRƯỚC khi cúp tới vẫn đang về.
        /// Lượt sau đọc mùa mới nên không bị chặn mãi; khi giờ máy chậm hơn mùa dịch vụ đang giữ thì kết quả chờ tới lúc giờ vượt mốc.</para>
        /// </summary>
        public async Task<SeasonResult> GetPendingSeasonResultAsync(CancellationToken cancellationToken)
        {
            await TryFlushAsync(cancellationToken);
            SeasonWindow currentSeason = CurrentSeason;
            if (HasUnsentTrophiesOutsideSeason(currentSeason.SeasonId)) return null;
            SeasonResult pendingResult = await GroupService.GetPendingResultAsync(currentSeason, cancellationToken);
            if (pendingResult == null) return null;
            if (GetUnsentTrophies(pendingResult.SeasonId) > 0) return null;
            // Kết quả thuộc ĐÚNG mùa đọc trước lượt gọi = mùa đó bị một lượt gọi khác khép trong lúc đang gọi. Cúp muộn của mùa có thể
            // đã được gửi xong trong lúc phản hồi còn trên đường về (hàng chờ giờ trống), nên kết quả trong tay có thể là bản tính
            // trước khi cúp tới. Bỏ nó, lượt sau (đọc mùa mới) hỏi lại được bản đã tính đủ cúp.
            if (string.Equals(pendingResult.SeasonId, currentSeason.SeasonId, StringComparison.Ordinal)) return null;
            return pendingResult;
        }

        public Task AcknowledgeSeasonResultAsync(string seasonId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(seasonId)) throw new ArgumentException("Season id không được rỗng.", nameof(seasonId));
            return GroupService.AcknowledgeResultAsync(seasonId, cancellationToken);
        }

        /// <summary>
        /// Nhận rương cuối mùa. Quà được ghi vào hàng chờ phát TRƯỚC khi đưa cho game, nên app tắt giữa chừng vẫn không mất quà;
        /// gọi lại nhiều lần không phát trùng.
        /// </summary>
        public async Task<LeagueClaimOutcome> ClaimSeasonRewardAsync(string seasonId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(seasonId)) throw new ArgumentException("Season id không được rỗng.", nameof(seasonId));
            string grantId = SeasonRewardGrantPrefix + seasonId;

            PendingLeagueReward alreadyPending = FindPendingReward(grantId);
            if (alreadyPending != null) return GrantOne(alreadyPending);

            LeagueRewardPackage package = await GroupService.ClaimSeasonRewardAsync(seasonId, cancellationToken) ?? LeagueRewardPackage.None;
            if (package.IsEmpty) return new LeagueClaimOutcome(LeagueClaimStatus.NothingToClaim, LeagueRewardPackage.None);

            var pending = new PendingLeagueReward(grantId, package);
            _state.PendingRewards.Add(pending);
            SaveAndNotify();
            return GrantOne(pending);
        }

        /// <summary>Phát lại các gói quà game chưa nhận được. Trả số gói đã phát.</summary>
        public int GrantPendingRewards()
        {
            if (_state.PendingRewards.Count == 0) return 0;
            int granted = 0;
            foreach (PendingLeagueReward pending in new List<PendingLeagueReward>(_state.PendingRewards))
            {
                if (GrantOne(pending).Status == LeagueClaimStatus.Granted) granted++;
            }
            return granted;
        }

        // ---------------------------------------------------------------- Công cụ debug / cheat

        /// <summary>Đặt streak trực tiếp, bỏ qua luật. Không phát quà bậc streak.</summary>
        public void DebugSetStreak(WinStreakState streak)
        {
            _state.Streak = new WinStreakState(StreakLadder.ClampLevel(streak.Level), streak.WinsTowardNextLevel);
            SaveAndNotify();
        }

        /// <summary>Xoá toàn bộ trạng thái trên máy (streak, cúp chờ gửi, quà chờ phát).</summary>
        public void DebugClearLocalState()
        {
            _state.Streak = WinStreakState.Empty;
            _state.StreakRunNumber = 0;
            _state.PendingTrophyGrants.Clear();
            _state.PendingRewards.Clear();
            RejectedTrophyGrantCount = 0;
            _textStore.Delete(_stateKey);
            StateChanged?.Invoke();
        }

        // ---------------------------------------------------------------- Nội bộ

        /// <summary>Lõi gửi, chạy bằng token riêng của lượt gửi dùng chung (xem <see cref="FlushPendingTrophiesAsync"/>).</summary>
        private async Task<int> FlushCoreAsync(CancellationToken cancellationToken)
        {
            int sent = 0;
            while (_state.PendingTrophyGrants.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LeagueTrophyGrant grant = _state.PendingTrophyGrants[0];
                try
                {
                    await GroupService.AddTrophiesAsync(CurrentSeason, grant, cancellationToken);
                }
                catch (LeagueTrophyGrantRejectedException rejection)
                {
                    // Từ chối dứt khoát, không phải lỗi mạng: gửi lại vô ích, còn giữ lại thì chặn mọi grant phía sau mãi mãi.
                    // Chỉ báo khi grant còn trong hàng chờ: lượt gửi cũ đã dừng mà lượt gọi dở của nó vẫn về thì không báo lần hai.
                    if (!RemovePendingTrophyGrant(grant.GrantId)) continue;
                    RejectedTrophyGrantCount++;
                    SaveAndNotify();
                    TrophyGrantRejected?.Invoke(grant, rejection.Reason);
                    continue;
                }
                RemovePendingTrophyGrant(grant.GrantId);
                sent++;
                SaveAndNotify();
            }
            return sent;
        }

        /// <summary>
        /// Gửi hàng chờ nếu có, nuốt lỗi mạng (không nuốt huỷ của CHÍNH người gọi). Dùng trước mọi lần hỏi dịch vụ về bảng / kết quả:
        /// với dịch vụ khép mùa theo lượt gọi, hỏi mùa mới khi còn cúp mùa cũ trong hàng chờ là khép mùa cũ thiếu cúp. Lượt gửi dùng
        /// chung không bao giờ bị huỷ bởi token của người gọi khác (xem <see cref="FlushPendingTrophiesAsync"/>), nên huỷ lọt tới đây
        /// mà token của mình chưa huỷ chỉ có thể là lỗi của dịch vụ (vd hết giờ chờ mạng) — xử lý như lỗi mạng.
        /// </summary>
        internal Task TryFlushPendingTrophiesAsync(CancellationToken cancellationToken)
        {
            return TryFlushAsync(cancellationToken);
        }

        private bool HasUnsentTrophiesOutsideSeason(string seasonId)
        {
            foreach (LeagueTrophyGrant grant in _state.PendingTrophyGrants)
            {
                if (!string.Equals(grant.SeasonId, seasonId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private async Task TryFlushAsync(CancellationToken cancellationToken)
        {
            if (_state.PendingTrophyGrants.Count == 0) return;
            try
            {
                await FlushPendingTrophiesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Mất mạng: vẫn cho tải trang / kiểm kết quả; cúp chờ gửi còn nguyên trong hàng đợi.
            }
        }

        private void ApplyStreak(WinStreakState before, WinStreakState after)
        {
            if (after.Level < before.Level) _state.StreakRunNumber++;
            _state.Streak = after;

            for (int level = before.Level + 1; level <= after.Level; level++)
            {
                LeagueRewardPackage reward = StreakLadder.RewardAt(level);
                if (reward.IsEmpty) continue;
                string grantId = StreakRewardGrantPrefix + _state.StreakRunNumber + "-" + level;
                if (FindPendingReward(grantId) == null) _state.PendingRewards.Add(new PendingLeagueReward(grantId, reward));
            }
        }

        private LeagueClaimOutcome GrantOne(PendingLeagueReward pending)
        {
            bool granted;
            try
            {
                granted = _rewardGranter.TryGrant(pending.GrantId, pending.Package);
            }
            catch (Exception)
            {
                granted = false;
            }
            if (!granted) return new LeagueClaimOutcome(LeagueClaimStatus.Deferred, pending.Package);

            _state.PendingRewards.Remove(pending);
            SaveAndNotify();
            return new LeagueClaimOutcome(LeagueClaimStatus.Granted, pending.Package);
        }

        private PendingLeagueReward FindPendingReward(string grantId)
        {
            foreach (PendingLeagueReward pending in _state.PendingRewards)
            {
                if (string.Equals(pending.GrantId, grantId, StringComparison.Ordinal)) return pending;
            }
            return null;
        }

        /// <summary>Bỏ grant khỏi hàng chờ. Trả false nếu grant không còn trong hàng chờ.</summary>
        private bool RemovePendingTrophyGrant(string grantId)
        {
            for (int index = 0; index < _state.PendingTrophyGrants.Count; index++)
            {
                if (!string.Equals(_state.PendingTrophyGrants[index].GrantId, grantId, StringComparison.Ordinal)) continue;
                _state.PendingTrophyGrants.RemoveAt(index);
                return true;
            }
            return false;
        }

        private void SaveAndNotify()
        {
            _textStore.Write(_stateKey, _state.Encode());
            StateChanged?.Invoke();
        }
    }
}
