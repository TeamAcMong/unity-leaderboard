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
        private Task<int> _activeFlush;

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

        public int UnsentTrophies
        {
            get
            {
                int total = 0;
                foreach (LeagueTrophyGrant grant in _state.PendingTrophyGrants) total += grant.Trophies;
                return total;
            }
        }

        /// <summary>Streak, cúp chờ gửi hoặc quà chờ phát vừa đổi.</summary>
        public event Action StateChanged;

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
                _state.PendingTrophyGrants.Add(new LeagueTrophyGrant(grantId, CurrentSeason.SeasonId, trophies));
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
        /// Gửi các lần cộng cúp đang chờ, theo thứ tự. Gọi chồng nhau thì dùng chung một lần gửi. Lỗi mạng ném ra ngoài; phần chưa
        /// gửi được vẫn nằm trong hàng chờ cho lần sau.
        /// </summary>
        public Task<int> FlushPendingTrophiesAsync(CancellationToken cancellationToken)
        {
            if (_activeFlush != null && !_activeFlush.IsCompleted) return _activeFlush;
            _activeFlush = FlushCoreAsync(cancellationToken);
            return _activeFlush;
        }

        /// <summary>Tải trang League. Gửi cúp chờ trước (lỗi thì bỏ qua, số chưa gửi nằm ở <see cref="LeaguePageData.UnsentTrophies"/>).</summary>
        public async Task<LeaguePageData> LoadPageAsync(CancellationToken cancellationToken)
        {
            await TryFlushAsync(cancellationToken);
            LeagueGroupSnapshot group = await GroupService.GetGroupAsync(CurrentSeason, cancellationToken);
            if (group == null) throw new InvalidOperationException("GetGroupAsync trả về null — dịch vụ nhóm phải ném lỗi thay vì trả null.");
            return new LeaguePageData(group, Rules, _state.Streak, StreakLadder, Clock.UtcNow, UnsentTrophies);
        }

        /// <summary>Kết quả mùa cũ còn việc cho UI (popup kết quả / rương). Cúp chờ gửi được gửi trước để mùa cũ khép đúng điểm.</summary>
        public async Task<SeasonResult> GetPendingSeasonResultAsync(CancellationToken cancellationToken)
        {
            await TryFlushAsync(cancellationToken);
            return await GroupService.GetPendingResultAsync(CurrentSeason, cancellationToken);
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
            _textStore.Delete(_stateKey);
            StateChanged?.Invoke();
        }

        // ---------------------------------------------------------------- Nội bộ

        private async Task<int> FlushCoreAsync(CancellationToken cancellationToken)
        {
            int sent = 0;
            while (_state.PendingTrophyGrants.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LeagueTrophyGrant grant = _state.PendingTrophyGrants[0];
                await GroupService.AddTrophiesAsync(CurrentSeason, grant, cancellationToken);
                RemovePendingTrophyGrant(grant.GrantId);
                sent++;
                SaveAndNotify();
            }
            return sent;
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

        private void RemovePendingTrophyGrant(string grantId)
        {
            for (int index = 0; index < _state.PendingTrophyGrants.Count; index++)
            {
                if (!string.Equals(_state.PendingTrophyGrants[index].GrantId, grantId, StringComparison.Ordinal)) continue;
                _state.PendingTrophyGrants.RemoveAt(index);
                return;
            }
        }

        private void SaveAndNotify()
        {
            _textStore.Write(_stateKey, _state.Encode());
            StateChanged?.Invoke();
        }
    }
}
