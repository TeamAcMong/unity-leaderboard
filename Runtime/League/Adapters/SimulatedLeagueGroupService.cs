using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace DreamTech.Leaderboard.League
{
    /// <summary>Tham số nhóm mô phỏng. Số liệu mặc định chỉ để chạy thử — game đặt từ config.</summary>
    public sealed class SimulatedLeagueOptions
    {
        public string LocalPlayerId = "local-player";
        public string LocalDisplayName = "You";

        /// <summary>Số người trong nhóm, tính cả người chơi.</summary>
        public int GroupSize = 30;

        public int Seed = 11;
        public int StartingTierIndex = 0;

        /// <summary>Cúp cuối mùa của bot yếu nhất / mạnh nhất ở tier thấp nhất.</summary>
        public int BotSeasonTrophiesMinimum = 10;
        public int BotSeasonTrophiesMaximum = 400;

        /// <summary>Mỗi tier cao hơn, cúp cuối mùa của bot nhân thêm hệ số này.</summary>
        public double TierTrophyGrowth = 1.35;

        /// <summary>Số mũ phân bố sức mạnh u^k: lớn hơn = nhiều bot yếu, ít bot mạnh.</summary>
        public double DistributionExponent = 1.8;

        /// <summary>Tỉ lệ bot không chơi cả mùa (0 cúp).</summary>
        public double IdleBotShare = 0.1;

        /// <summary>Bot bắt đầu kiếm cúp muộn nhất ở tiến độ mùa này (0.3 = trong 30% đầu mùa).</summary>
        public double LatestBotStartProgress = 0.3;

        /// <summary>Nhịp kiếm cúp: số mũ &lt; 1 = kiếm nhiều đầu mùa, &gt; 1 = dồn cuối mùa. Mỗi bot lấy ngẫu nhiên trong khoảng.</summary>
        public double BotPaceExponentMinimum = 0.7;
        public double BotPaceExponentMaximum = 1.4;

        /// <summary>Độ trễ mạng giả lập (ms). 0 = hoàn tất đồng bộ (test).</summary>
        public int LatencyMilliseconds = 0;

        /// <summary>
        /// Số mùa đã khép giữ lại (kết quả mùa + sổ cúp của mùa đó, để nhận grant tới muộn và chống cộng trùng khi gửi lại). Chỉ sổ
        /// đã hết việc cho UI (kết quả đã xem + đã nhận, hoặc không có kết quả) bị bỏ để giữ giới hạn, cũ trước; mùa vừa khép gần
        /// nhất luôn được giữ. Sổ còn kết quả chờ không bao giờ bị bỏ, nên số sổ có thể tạm vượt giới hạn (vd gửi bù cúp của nhiều
        /// mùa bị nhảy qua cùng lúc) — xem / nhận xong các kết quả đó thì tự thu gọn lại.
        /// </summary>
        public int MaximumStoredResults = 4;

        public string StoreKey = "league.simulation";

        public string[] FirstNames =
        {
            "Minh", "Linh", "Huy", "Trang", "Bao", "Khoa", "Vy", "Tuan", "An", "Mai", "Nam", "Thao",
            "Kenji", "Aiko", "Sofia", "Lucas", "Noah", "Emma", "Leo", "Mia", "Kai", "Zoe", "Liam", "Nora",
        };

        public string[] NameSuffixes = { "", "", "", "Pro", "_x", "99", "Star", "King", "Queen" };

        public SimulatedLeagueOptions Clone()
        {
            var copy = (SimulatedLeagueOptions)MemberwiseClone();
            copy.FirstNames = FirstNames != null ? (string[])FirstNames.Clone() : Array.Empty<string>();
            copy.NameSuffixes = NameSuffixes != null ? (string[])NameSuffixes.Clone() : Array.Empty<string>();
            return copy;
        }
    }

    /// <summary>
    /// Lỗi tạm thời của dịch vụ mô phỏng (gọi lại được): lỗi mạng giả lập do <see cref="SimulatedLeagueGroupService.FailNextCall"/>,
    /// lượt gọi mang mùa chưa bắt đầu theo đồng hồ của dịch vụ (lượt gọi cũ đọc giờ đã tua rồi đồng hồ bị đặt lại), hoặc lượt gọi
    /// bắt đầu trước <see cref="SimulatedLeagueGroupService.DebugResetSimulation"/>.
    /// </summary>
    public sealed class SimulatedLeagueException : Exception
    {
        public SimulatedLeagueException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Dịch vụ nhóm chạy offline: nhóm gồm người chơi + bot. Cúp của bot là hàm tất định của (seed, mùa, tier, người chơi, thời
    /// điểm) — không lưu bot, mở lại app hay cài lại vẫn ra cùng bảng, và cúp bot chỉ tăng theo thời gian. Máy chỉ lưu bậc hiện
    /// tại, mùa đang giữ (cúp + grant đã nhận), và sổ của vài mùa vừa khép (cửa sổ mùa, bậc, cúp, grant, kết quả).
    ///
    /// <para><b>Mùa chỉ tiến.</b> Mùa theo đồng hồ mới hơn mùa đang giữ → khép mùa đang giữ rồi mở mùa mới. Mùa theo đồng hồ cũ
    /// hơn (đồng hồ lùi) → giữ nguyên mùa đang giữ, không mở lại mùa đã khép, không xoá cúp. Mỗi mùa có một sổ duy nhất nên
    /// không bao giờ có hai kết quả cùng một mùa. Grant tới muộn cho mùa đã khép được tính lại từ sổ đó (xem hợp đồng ở
    /// <see cref="ILeagueGroupService"/>). Grant của một mùa bị "nhảy qua" (chưa từng giữ vì mọi lần gửi trong mùa đó thất bại) được
    /// dựng sổ riêng từ cửa sổ mùa mà grant mang theo (<see cref="LeagueTrophyGrant.Season"/>), chèn đúng thứ tự thời gian, miễn là mọi
    /// mùa phía sau nó còn trống — kể cả khi một lượt gọi khác đã kịp mở / khép những mùa trống đó trước.</para>
    ///
    /// <para><b>Đồng hồ.</b> Dịch vụ và <see cref="LeagueSystem"/> phải dùng CÙNG một đồng hồ (thường là
    /// <see cref="MonotonicLeagueClock"/>). Lượt gọi mang mùa chưa bắt đầu theo đồng hồ này bị từ chối bằng
    /// <see cref="SimulatedLeagueException"/> (lỗi tạm): đó là lượt gọi cũ đã đọc giờ tua tới rồi giờ bị đặt lại, cho qua thì dịch vụ
    /// kẹt ở mùa tương lai. Lượt gọi bắt đầu trước <see cref="DebugResetSimulation"/> thất bại bằng
    /// <see cref="SimulatedLeagueException"/> (lỗi tạm, không phải <see cref="OperationCanceledException"/>) và không ghi gì.</para>
    ///
    /// <para>Muốn lên backend thật: viết một class khác cài <see cref="ILeagueGroupService"/> và đổi dòng
    /// <c>WithGroupService</c> ở composition root. Contract test của port chạy cho cả hai.</para>
    /// </summary>
    public sealed class SimulatedLeagueGroupService : ILeagueGroupService
    {
        private readonly SimulatedLeagueOptions _options;
        private readonly LeagueRules _rules;
        private readonly ILeagueClock _clock;
        private readonly ILeagueTextStore _store;
        private readonly SimulatedLeagueData _data;
        private bool _failNextCall;

        /// <summary>Tăng mỗi lần <see cref="DebugResetSimulation"/>: lượt gọi bắt đầu ở thế hệ cũ không được ghi lên dữ liệu mới.</summary>
        private int _resetGeneration;

        public SimulatedLeagueGroupService(SimulatedLeagueOptions options, LeagueRules rules, ILeagueClock clock, ILeagueTextStore store)
        {
            _options = (options ?? new SimulatedLeagueOptions()).Clone();
            if (_options.GroupSize < 1) throw new ArgumentOutOfRangeException(nameof(options), "Nhóm phải có ít nhất 1 người.");
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            LatencyMilliseconds = _options.LatencyMilliseconds;
            _data = _store.TryRead(_options.StoreKey, out string stored) ? SimulatedLeagueData.Decode(stored) : null;
            if (_data == null) _data = new SimulatedLeagueData { TierIndex = _rules.Ladder.ClampIndex(_options.StartingTierIndex) };
        }

        public string LocalPlayerId => _options.LocalPlayerId;
        public int LatencyMilliseconds { get; set; }

        /// <summary>Bậc của mùa đang chạy (hoặc mùa sắp bắt đầu nếu chưa vào nhóm).</summary>
        public int CurrentTierIndex => _data.HasActiveSeason ? _data.ActiveTierIndex : _data.TierIndex;

        public long LocalTrophies => _data.HasActiveSeason ? _data.LocalTrophies : 0;

        /// <summary>Id mùa dịch vụ đang giữ; null nếu chưa vào nhóm. Có thể khác mùa theo đồng hồ khi đồng hồ bị lùi.</summary>
        public string ActiveSeasonId => _data.HasActiveSeason ? _data.ActiveSeasonId : null;

        // ---------------------------------------------------------------- ILeagueGroupService

        public async Task<LeagueGroupSnapshot> GetGroupAsync(SeasonWindow currentSeason, CancellationToken cancellationToken)
        {
            if (currentSeason == null) throw new ArgumentNullException(nameof(currentSeason));
            await BeginCallAsync(cancellationToken);
            EnsureActiveSeason(currentSeason);
            return BuildActiveSnapshot(_clock.UtcNow);
        }

        public async Task<LeagueGroupSnapshot> AddTrophiesAsync(SeasonWindow currentSeason, LeagueTrophyGrant grant, CancellationToken cancellationToken)
        {
            if (currentSeason == null) throw new ArgumentNullException(nameof(currentSeason));
            if (string.IsNullOrEmpty(grant.GrantId)) throw new ArgumentException("Grant chưa được khởi tạo.", nameof(grant));
            await BeginCallAsync(cancellationToken);

            // Kiểm mùa TRƯỚC khi cộng: mùa tương lai (lượt gọi cũ) mà lọt vào TryApplyGrant thì có thể đã khép mùa / cộng cúp rồi mới
            // bị từ chối ở EnsureActiveSeason.
            ThrowIfSeasonNotStarted(currentSeason);
            bool accepted = TryApplyGrant(currentSeason, grant, out LeagueTrophyGrantRejection rejection);
            EnsureActiveSeason(currentSeason);
            Save();

            if (!accepted) throw new LeagueTrophyGrantRejectedException(grant, rejection, DescribeRejection(grant, rejection));
            return BuildActiveSnapshot(_clock.UtcNow);
        }

        public async Task<SeasonResult> GetPendingResultAsync(SeasonWindow currentSeason, CancellationToken cancellationToken)
        {
            if (currentSeason == null) throw new ArgumentNullException(nameof(currentSeason));
            await BeginCallAsync(cancellationToken);
            EnsureActiveSeason(currentSeason);
            foreach (SimulatedClosedSeason closed in _data.ClosedSeasons)
            {
                if (closed.HasPendingResult) return closed.Result;
            }
            return null;
        }

        public async Task AcknowledgeResultAsync(string seasonId, CancellationToken cancellationToken)
        {
            await BeginCallAsync(cancellationToken);
            SimulatedClosedSeason closed = FindClosedSeasonWithResult(seasonId, requireUnacknowledged: true, requireUnclaimedReward: false);
            if (closed == null) return;
            closed.Result = closed.Result.WithAcknowledged();
            TrimClosedSeasons();
            Save();
        }

        public async Task<LeagueRewardPackage> ClaimSeasonRewardAsync(string seasonId, CancellationToken cancellationToken)
        {
            await BeginCallAsync(cancellationToken);
            SimulatedClosedSeason closed = FindClosedSeasonWithResult(seasonId, requireUnacknowledged: false, requireUnclaimedReward: true);
            if (closed == null) return LeagueRewardPackage.None;
            SeasonResult result = closed.Result;
            closed.Result = result.WithRewardClaimed();
            TrimClosedSeasons();
            Save();
            return result.Reward;
        }

        // ---------------------------------------------------------------- Công cụ debug / cheat

        /// <summary>Lần gọi kế tiếp ném <see cref="SimulatedLeagueException"/>.</summary>
        public void FailNextCall()
        {
            _failNextCall = true;
        }

        /// <summary>Đặt cúp người chơi của mùa đang giữ (tự vào nhóm của <paramref name="currentSeason"/> nếu chưa).</summary>
        public void DebugSetLocalTrophies(SeasonWindow currentSeason, long trophies)
        {
            EnsureActiveSeason(currentSeason);
            _data.LocalTrophies = Math.Max(0, trophies);
            Save();
        }

        /// <summary>Đổi bậc của mùa đang chạy và các mùa sau. Bảng bot sinh lại theo bậc mới.</summary>
        public void DebugSetTierIndex(SeasonWindow currentSeason, int tierIndex)
        {
            EnsureActiveSeason(currentSeason);
            int clamped = _rules.Ladder.ClampIndex(tierIndex);
            _data.TierIndex = clamped;
            _data.ActiveTierIndex = clamped;
            Save();
        }

        /// <summary>
        /// Cúp cần có NGAY BÂY GIỜ để đứng ở hạng <paramref name="targetRank"/> (0-based) — cheat "leo lên hạng N". Gần đúng khi bot
        /// hoà cúp (hoà thì người chơi đứng sau bot).
        /// </summary>
        /// <param name="projectToSeasonEnd">
        /// false = đủ để đứng hạng đó ngay lúc này (bot còn kiếm cúp tiếp nên hạng sẽ trôi xuống);
        /// true = tính theo bảng lúc mùa kết thúc, tức là đủ để GIỮ hạng đó tới cuối mùa. Kết quả mùa luôn tính theo bảng lúc
        /// kết thúc, nên cheat "hết mùa" ngay sau khi leo bằng false thường ra hạng thấp hơn hạng vừa thấy trên trang.
        /// </param>
        public long DebugTrophiesToReachRank(SeasonWindow currentSeason, int targetRank, bool projectToSeasonEnd = false)
        {
            EnsureActiveSeason(currentSeason);
            SeasonWindow window = ActiveWindow();
            List<SimulatedPlayer> bots = BuildBots(window, _data.ActiveTierIndex, projectToSeasonEnd ? window.EndUtc : _clock.UtcNow);
            bots.Sort(ComparePlayers);
            if (bots.Count == 0 || targetRank >= bots.Count) return 0;
            if (targetRank < 0) targetRank = 0;
            return bots[targetRank].Trophies + 1;
        }

        /// <summary>
        /// Xoá toàn bộ dữ liệu mô phỏng (bậc, cúp, mùa đang giữ, sổ các mùa đã khép, kết quả). Lượt gọi đang chạy dở (bắt đầu trước
        /// lúc xoá) thất bại bằng <see cref="SimulatedLeagueException"/> (lỗi tạm, gọi lại được) khi hết độ trễ và không ghi lên dữ
        /// liệu mới. Không dùng <see cref="OperationCanceledException"/>: token của nơi gọi không bị huỷ, host coi huỷ là "người dùng
        /// huỷ" sẽ im lặng đứng ở trạng thái đang tải.
        /// </summary>
        public void DebugResetSimulation()
        {
            Interlocked.Increment(ref _resetGeneration);
            _store.Delete(_options.StoreKey);
            _data.ClearActiveSeason();
            _data.ClosedSeasons.Clear();
            _data.TierIndex = _rules.Ladder.ClampIndex(_options.StartingTierIndex);
        }

        // ---------------------------------------------------------------- Cộng cúp

        private bool TryApplyGrant(SeasonWindow currentSeason, in LeagueTrophyGrant grant, out LeagueTrophyGrantRejection rejection)
        {
            rejection = default;

            // 1. Mùa đã khép: sổ của mùa đó là nguồn sự thật.
            SimulatedClosedSeason closed = _data.FindClosedSeason(grant.SeasonId);
            if (closed != null) return TryApplyLateGrant(closed, grant, out rejection);

            // 2. Mùa đang giữ — kể cả khi đã hết theo đồng hồ mà chưa khép: thắng lúc 23:59 gửi lúc 00:01 vẫn tính cho mùa đó.
            //    Mùa được khép ngay sau (EnsureActiveSeason ở nơi gọi); grant cùng mùa tới sau đó đi đường 1.
            if (_data.HasActiveSeason && IsSameSeason(grant.SeasonId, _data.ActiveSeasonId))
            {
                ApplyToActiveSeason(grant);
                return true;
            }

            // 3. Grant của mùa hiện tại trong khi dịch vụ còn giữ mùa cũ đã hết: khép mùa cũ TRƯỚC rồi mới cộng vào mùa mới.
            //    Làm ngược lại (cộng trước, khép sau) thì grant không khớp mùa nào và bị bỏ — lỗi "mất cúp đầu mùa" của 0.2.0.
            if (IsSameSeason(grant.SeasonId, currentSeason.SeasonId))
            {
                EnsureActiveSeason(currentSeason);
                if (_data.HasActiveSeason && IsSameSeason(grant.SeasonId, _data.ActiveSeasonId))
                {
                    ApplyToActiveSeason(grant);
                    return true;
                }
            }

            // 4. Mùa bị "nhảy qua": dịch vụ chưa từng giữ vì mọi lần gửi trong mùa đó đều thất bại.
            if (TryApplySkippedSeasonGrant(currentSeason, grant)) return true;

            rejection = LeagueTrophyGrantRejection.UnknownSeason;
            return false;
        }

        /// <summary>
        /// Grant của một mùa dịch vụ chưa từng giữ, nằm trước mùa hiện tại — vd thắng ở N+1, gửi thất bại, lần gửi được đầu tiên rơi
        /// vào mùa sau nữa. Nhận khi MỌI mùa dịch vụ biết mà bắt đầu sau khi mùa của grant kết thúc (mùa đang giữ và các sổ đã khép)
        /// đều còn TRỐNG (<see cref="IsEmptyClosedSeason"/>, <see cref="IsActiveSeasonEmpty"/>): khi đó các mùa đó chưa ghi nhận gì
        /// phụ thuộc vào bậc, nên chèn sổ của mùa bị nhảy qua vào giữa rồi tính lại chúng cho ra đúng dữ liệu như thể grant đã tới
        /// đúng lúc. Kết quả không phụ thuộc thứ tự lượt gọi: một lượt GetGroup / gửi hỏng ở mùa sau chạy trước grant (dịch vụ đã
        /// giữ, hoặc đã khép, những mùa trống phía sau) cũng ra cùng dữ liệu. Các trạng thái được phủ:
        /// <list type="bullet">
        /// <item>(a) còn giữ N (mùa trước grant): khép N trước, N thành sổ liền trước;</item>
        /// <item>(b) đã khép N và đang giữ một mùa trống phía sau: chèn sổ chen giữa;</item>
        /// <item>(c) giữa sổ của grant và mùa đang giữ còn sổ trống đã khép (mùa trống đã mở rồi khép trong lúc gửi vẫn hỏng);</item>
        /// <item>chưa khép mùa nào và mùa đầu tiên dịch vụ giữ là mùa trống phía sau grant.</item>
        /// </list>
        /// Bậc của sổ mới = bậc mùa sau của sổ khép liền trước; không có sổ liền trước thì lấy bậc mà mùa trống đầu tiên phía sau đã
        /// bắt đầu. Sau đó bậc được truyền qua các mùa trống phía sau (<see cref="PropagateTierToEmptySeasonsAfter"/>).
        /// <para>Mùa phía sau đã có cúp / grant / kết quả đã chốt → KHÔNG nhận (grant bị từ chối <see cref="LeagueTrophyGrantRejection.UnknownSeason"/>):
        /// người chơi đã chơi hoặc đã xem kết quả ở bậc tính khi chưa có grant này, chèn sổ vào là đổi lịch sử đã chốt. Chồng lấn cửa
        /// sổ mùa (lịch mùa đổi) cũng không nhận. Cần cửa sổ mùa của grant (<see cref="LeagueTrophyGrant.Season"/>): id mùa là chuỗi,
        /// không so thứ tự thời gian được.</para>
        /// </summary>
        private bool TryApplySkippedSeasonGrant(SeasonWindow currentSeason, in LeagueTrophyGrant grant)
        {
            SeasonWindow grantSeason = grant.Season;
            if (grantSeason == null || grantSeason.EndUtc > currentSeason.StartUtc) return false;

            // 1. Kiểm hết TRƯỚC khi đổi dữ liệu: mùa phía sau phải trống, không chồng lấn.
            bool hasClosedSeasonAfterGrant = false;
            foreach (SimulatedClosedSeason closed in _data.ClosedSeasons)
            {
                if (closed.EndUtc <= grantSeason.StartUtc) continue;
                if (closed.StartUtc < grantSeason.EndUtc || !IsEmptyClosedSeason(closed)) return false;
                hasClosedSeasonAfterGrant = true;
            }

            bool heldEndedBeforeGrant = false;
            if (_data.HasActiveSeason)
            {
                heldEndedBeforeGrant = _data.ActiveSeasonEnd <= grantSeason.StartUtc;
                bool heldStartsAfterGrant = _data.ActiveSeasonStart >= grantSeason.EndUtc;
                if (!heldEndedBeforeGrant && (!heldStartsAfterGrant || !IsActiveSeasonEmpty())) return false;

                // Sổ đã khép nằm SAU mùa đang giữ chỉ có ở dữ liệu hỏng (mùa chỉ tiến) — không đoán thứ tự, không nhận.
                if (heldEndedBeforeGrant && hasClosedSeasonAfterGrant) return false;
            }

            // 2. Trạng thái (a): mùa đang giữ đã hết trước mùa của grant → khép nó trước, nó thành sổ liền trước.
            if (heldEndedBeforeGrant) FinalizeActiveSeason();

            // 3. Dựng sổ của mùa bị nhảy qua, chèn đúng thứ tự thời gian, rồi truyền bậc qua các mùa trống phía sau.
            int tierIndex = _rules.Ladder.ClampIndex(TierIndexForSeasonStartingAt(grantSeason));
            var skipped = new SimulatedClosedSeason(grantSeason.SeasonId, grantSeason.StartUtc, grantSeason.EndUtc, tierIndex)
            {
                LocalTrophies = grant.Trophies,
            };
            skipped.AppliedGrantIds.Add(grant.GrantId);
            DecideClosedSeason(skipped);
            _data.TryAddClosedSeason(skipped);
            PropagateTierToEmptySeasonsAfter(skipped);
            TrimClosedSeasons();
            return true;
        }

        /// <summary>
        /// Bậc mà một mùa chen vào dòng thời gian phải có: bậc mùa sau của sổ khép liền trước; không có sổ liền trước thì bậc mà mùa
        /// đầu tiên phía sau đã bắt đầu (sổ đã khép đầu tiên, rồi tới mùa đang giữ); không biết mùa nào thì bậc của mùa sắp bắt đầu.
        /// </summary>
        private int TierIndexForSeasonStartingAt(SeasonWindow season)
        {
            SimulatedClosedSeason previous = null;
            SimulatedClosedSeason firstAfter = null;
            foreach (SimulatedClosedSeason closed in _data.ClosedSeasons)
            {
                if (closed.EndUtc <= season.StartUtc) previous = closed;
                else if (firstAfter == null && closed.StartUtc >= season.EndUtc) firstAfter = closed;
            }

            if (previous != null) return previous.NextTierIndex;
            if (firstAfter != null) return firstAfter.TierIndex;
            return _data.HasActiveSeason && _data.ActiveSeasonStart >= season.EndUtc ? _data.ActiveTierIndex : _data.TierIndex;
        }

        /// <summary>Sổ đã khép chưa ghi nhận gì phụ thuộc vào bậc: không cúp, không grant, kết quả (nếu có) chưa chốt với người chơi.</summary>
        private static bool IsEmptyClosedSeason(SimulatedClosedSeason closed)
        {
            return closed.LocalTrophies == 0 && closed.AppliedGrantIds.Count == 0 && !closed.IsFinalized;
        }

        /// <summary>Mùa đang giữ chưa có cúp, chưa nhận grant nào: đổi bậc của nó không làm sai thứ gì người chơi đã kiếm.</summary>
        private bool IsActiveSeasonEmpty()
        {
            return _data.LocalTrophies == 0 && _data.AppliedGrantIds.Count == 0;
        }

        private void ApplyToActiveSeason(in LeagueTrophyGrant grant)
        {
            if (!_data.AppliedGrantIds.Add(grant.GrantId)) return;
            _data.LocalTrophies += grant.Trophies;
        }

        /// <summary>Grant tới sau khi mùa của nó đã khép. Chưa chốt với người chơi → cộng + tính lại kết quả; đã chốt → từ chối.</summary>
        private bool TryApplyLateGrant(SimulatedClosedSeason closed, in LeagueTrophyGrant grant, out LeagueTrophyGrantRejection rejection)
        {
            rejection = default;
            if (closed.AppliedGrantIds.Contains(grant.GrantId)) return true;

            if (closed.IsFinalized)
            {
                rejection = LeagueTrophyGrantRejection.SeasonAlreadyFinalized;
                return false;
            }

            int previousNextTierIndex = closed.NextTierIndex;
            closed.AppliedGrantIds.Add(grant.GrantId);
            closed.LocalTrophies += grant.Trophies;
            DecideClosedSeason(closed);
            if (closed.NextTierIndex != previousNextTierIndex) PropagateTierToEmptySeasonsAfter(closed);
            return true;
        }

        // ---------------------------------------------------------------- Mùa

        private void EnsureActiveSeason(SeasonWindow currentSeason)
        {
            ThrowIfSeasonNotStarted(currentSeason);
            if (!_data.HasActiveSeason)
            {
                StartActiveSeason(currentSeason);
                Save();
                return;
            }
            if (IsSameSeason(_data.ActiveSeasonId, currentSeason.SeasonId)) return;

            // Mùa chỉ tiến. Mùa theo đồng hồ chưa bắt đầu sau khi mùa đang giữ hết (đồng hồ lùi, hoặc lịch mùa đổi làm hai mùa
            // chồng nhau) → GIỮ NGUYÊN mùa đang giữ cùng số cúp. Bản 0.2.0 bỏ mùa đang giữ rồi mở lại mùa cũ: mùa đã khép bị mở
            // lại với 0 cúp, khép lần hai sinh kết quả trùng và luồng popup lặp mãi.
            if (currentSeason.StartUtc < _data.ActiveSeasonEnd) return;

            FinalizeActiveSeason();
            StartActiveSeason(currentSeason);
            Save();
        }

        /// <summary>
        /// Từ chối mùa chưa bắt đầu theo đồng hồ của CHÍNH dịch vụ. Với đồng hồ dùng chung (cách lắp chuẩn) điều này không xảy ra
        /// khi chơi thường; chỉ gặp khi một lượt gọi đọc mùa lúc giờ đã tua tới, rồi giờ bị đặt lại trước khi lượt đó chạy tới đây.
        /// Cho qua thì dịch vụ mở (hoặc khép sớm mùa đang giữ để mở) mùa tương lai và kẹt ở đó — mùa chỉ tiến. Ném lỗi tạm thay vì
        /// từ chối grant: <see cref="LeagueSystem"/> giữ grant trong hàng chờ, lần gọi sau đọc lại mùa theo giờ mới.
        /// </summary>
        private void ThrowIfSeasonNotStarted(SeasonWindow currentSeason)
        {
            if (currentSeason.StartUtc <= _clock.UtcNow) return;
            throw new SimulatedLeagueException("Mùa " + currentSeason.SeasonId +
                                               " chưa bắt đầu theo đồng hồ của dịch vụ (lượt gọi cũ mang giờ đã tua) — gọi lại để đọc mùa mới.");
        }

        private void StartActiveSeason(SeasonWindow season)
        {
            _data.ActiveSeasonId = season.SeasonId;
            _data.ActiveSeasonStart = season.StartUtc;
            _data.ActiveSeasonEnd = season.EndUtc;
            _data.ActiveTierIndex = _rules.Ladder.ClampIndex(_data.TierIndex);
            _data.LocalTrophies = 0;
            _data.AppliedGrantIds.Clear();
        }

        private void FinalizeActiveSeason()
        {
            // Mùa đang giữ trùng id một sổ đã khép chỉ có ở dữ liệu hỏng (mùa chỉ tiến nên không bao giờ mở lại mùa đã khép). Mọi
            // grant của id đó đã vào sổ (đường 1 của TryApplyGrant), mùa đang giữ không có cúp nào → bỏ, không sinh kết quả thứ hai.
            if (_data.FindClosedSeason(_data.ActiveSeasonId) != null)
            {
                _data.ClearActiveSeason();
                return;
            }

            var closed = new SimulatedClosedSeason(_data.ActiveSeasonId, _data.ActiveSeasonStart, _data.ActiveSeasonEnd, _data.ActiveTierIndex)
            {
                LocalTrophies = _data.LocalTrophies,
            };
            closed.AppliedGrantIds.UnionWith(_data.AppliedGrantIds);
            DecideClosedSeason(closed);

            _data.TierIndex = closed.NextTierIndex;
            _data.TryAddClosedSeason(closed);
            _data.ClearActiveSeason();
            TrimClosedSeasons();
        }

        /// <summary>
        /// Tính kết quả của một mùa đã khép từ sổ của nó: bảng bot tất định lúc mùa kết thúc + cúp của người chơi. Chỉ gọi khi kết
        /// quả chưa chốt với người chơi (cờ xem / nhận của kết quả mới luôn là false).
        /// </summary>
        private void DecideClosedSeason(SimulatedClosedSeason closed)
        {
            SeasonWindow window = closed.Window;
            LeagueGroupSnapshot finalStandings = BuildSnapshot(window, closed.TierIndex, closed.LocalTrophies, window.EndUtc);
            bool participated = closed.LocalTrophies > 0;
            var input = new SeasonOutcomeInput(closed.TierIndex, finalStandings.LocalRank, finalStandings.GroupSize, closed.LocalTrophies,
                                               participated);
            SeasonOutcomeDecision decision = _rules.Decide(input);
            LeagueRewardPackage reward = participated
                ? _rules.RewardFor(closed.TierIndex, finalStandings.LocalRank, finalStandings.GroupSize)
                : LeagueRewardPackage.None;

            closed.NextTierIndex = decision.NextTierIndex;
            closed.Result = participated || decision.Outcome != SeasonOutcome.Unchanged
                ? new SeasonResult(closed.SeasonId, closed.TierIndex, decision.NextTierIndex, decision.Outcome, finalStandings.LocalRank,
                                   finalStandings.GroupSize, closed.LocalTrophies, reward, acknowledged: false, rewardClaimed: false)
                : null;
        }

        /// <summary>
        /// Bậc mùa sau của <paramref name="changed"/> vừa được tính (lại): truyền theo thứ tự thời gian qua các mùa phía sau CÒN TRỐNG
        /// — sổ trống đã khép được đặt lại bậc và tính lại kết quả (bảng bot và outcome đổi theo bậc, nên kết quả có thể xuất hiện
        /// hoặc biến mất đúng luật), mùa đang giữ trống đổi bậc. Gặp mùa đã có cúp / grant / kết quả đã chốt thì dừng: người chơi đã
        /// kiếm cúp hoặc đã xem kết quả ở bậc cũ, không đổi bậc giữa chừng.
        /// </summary>
        private void PropagateTierToEmptySeasonsAfter(SimulatedClosedSeason changed)
        {
            int changedIndex = _data.ClosedSeasons.IndexOf(changed);
            if (changedIndex < 0) return;

            int tierIndex = changed.NextTierIndex;
            for (int index = changedIndex + 1; index < _data.ClosedSeasons.Count; index++)
            {
                SimulatedClosedSeason later = _data.ClosedSeasons[index];
                if (!IsEmptyClosedSeason(later)) return;
                later.TierIndex = _rules.Ladder.ClampIndex(tierIndex);
                DecideClosedSeason(later);
                tierIndex = later.NextTierIndex;
            }

            if (_data.HasActiveSeason)
            {
                if (_data.ActiveSeasonStart < changed.EndUtc || !IsActiveSeasonEmpty()) return;
                _data.ActiveTierIndex = _rules.Ladder.ClampIndex(tierIndex);
            }
            _data.TierIndex = tierIndex;
        }

        /// <summary>
        /// Thu gọn sổ về <see cref="SimulatedLeagueOptions.MaximumStoredResults"/> mùa bằng cách bỏ (cũ trước) những sổ đã hết việc cho
        /// UI. Sổ còn kết quả chờ (<see cref="SimulatedClosedSeason.HasPendingResult"/>) KHÔNG BAO GIỜ bị bỏ, kể cả khi số sổ vượt giới
        /// hạn: bỏ nó là mất kết quả + rương người chơi chưa thấy. Bản trước bỏ luôn sổ cũ nhất khi mọi sổ đều còn chờ — gửi bù cúp của
        /// nhiều mùa bị nhảy qua cùng lúc (mỗi mùa dựng một sổ có kết quả) đẩy sổ của mùa trước đó, đã có cúp và kết quả chưa xem, ra khỏi
        /// danh sách. Số sổ vượt giới hạn tự thu gọn ở lượt sau: hàm này chạy lại sau mỗi lần xem / nhận.
        /// </summary>
        private void TrimClosedSeasons()
        {
            List<SimulatedClosedSeason> closedSeasons = _data.ClosedSeasons;
            int maximum = Math.Max(1, _options.MaximumStoredResults);

            // Mùa vừa khép gần nhất (phần tử cuối) không bị bỏ ở lượt này: grant tới muộn của nó vẫn cần sổ.
            for (int index = 0; index < closedSeasons.Count - 1 && closedSeasons.Count > maximum;)
            {
                if (!closedSeasons[index].HasPendingResult) closedSeasons.RemoveAt(index);
                else index++;
            }
        }

        private SimulatedClosedSeason FindClosedSeasonWithResult(string seasonId, bool requireUnacknowledged, bool requireUnclaimedReward)
        {
            foreach (SimulatedClosedSeason closed in _data.ClosedSeasons)
            {
                if (closed.Result == null || !IsSameSeason(closed.SeasonId, seasonId)) continue;
                if (requireUnacknowledged && closed.Result.Acknowledged) continue;
                if (requireUnclaimedReward && !closed.Result.HasUnclaimedReward) continue;
                return closed;
            }
            return null;
        }

        private SeasonWindow ActiveWindow()
        {
            return new SeasonWindow(_data.ActiveSeasonId, _data.ActiveSeasonStart, _data.ActiveSeasonEnd);
        }

        private static bool IsSameSeason(string first, string second)
        {
            return string.Equals(first, second, StringComparison.Ordinal);
        }

        private static string DescribeRejection(in LeagueTrophyGrant grant, LeagueTrophyGrantRejection rejection)
        {
            string reason = rejection == LeagueTrophyGrantRejection.SeasonAlreadyFinalized
                ? "kết quả mùa đã được chốt với người chơi"
                : "dịch vụ không nhận mùa này";
            return "Grant " + grant.GrantId + " (+" + grant.Trophies.ToString(CultureInfo.InvariantCulture) + " cúp, mùa " + grant.SeasonId +
                   ") bị từ chối: " + reason + ".";
        }

        // ---------------------------------------------------------------- Bảng xếp hạng

        private LeagueGroupSnapshot BuildActiveSnapshot(DateTime atUtc)
        {
            return BuildSnapshot(ActiveWindow(), _data.ActiveTierIndex, _data.LocalTrophies, atUtc);
        }

        private LeagueGroupSnapshot BuildSnapshot(SeasonWindow window, int tierIndex, long localTrophies, DateTime atUtc)
        {
            List<SimulatedPlayer> players = BuildBots(window, tierIndex, atUtc);
            players.Add(new SimulatedPlayer(_options.LocalPlayerId, _options.LocalDisplayName, localTrophies, int.MaxValue));
            players.Sort(ComparePlayers);

            var standings = new List<LeaderboardEntry>(players.Count);
            for (int rank = 0; rank < players.Count; rank++)
            {
                SimulatedPlayer player = players[rank];
                standings.Add(new LeaderboardEntry(player.PlayerId, player.DisplayName, player.Trophies, rank));
            }
            return new LeagueGroupSnapshot(window, tierIndex, standings, _options.LocalPlayerId);
        }

        /// <summary>
        /// Sinh bot tất định. Mỗi bot luôn rút đủ số giá trị ngẫu nhiên theo cùng thứ tự (kể cả bot không chơi) để bảng không đổi
        /// khi chỉ đổi tham số của một bot.
        /// </summary>
        private List<SimulatedPlayer> BuildBots(SeasonWindow window, int tierIndex, DateTime atUtc)
        {
            string seedText = _options.Seed.ToString(CultureInfo.InvariantCulture) + "|" + window.SeasonId + "|" +
                              tierIndex.ToString(CultureInfo.InvariantCulture) + "|" + _options.LocalPlayerId;
            var random = new Random(StableHash(seedText));
            string[] firstNames = _options.FirstNames != null && _options.FirstNames.Length > 0 ? _options.FirstNames : new[] { "Player" };
            string[] suffixes = _options.NameSuffixes != null && _options.NameSuffixes.Length > 0 ? _options.NameSuffixes : new[] { "" };

            double progress = window.Progress(atUtc);
            double tierScale = Math.Pow(Math.Max(0.0, _options.TierTrophyGrowth), Math.Max(0, tierIndex));
            double minimum = Math.Max(0, _options.BotSeasonTrophiesMinimum);
            double maximum = Math.Max(minimum, _options.BotSeasonTrophiesMaximum);
            double latestStart = Clamp01(_options.LatestBotStartProgress);

            int botCount = Math.Max(0, _options.GroupSize - 1);
            var bots = new List<SimulatedPlayer>(botCount + 1);
            for (int index = 0; index < botCount; index++)
            {
                string name = firstNames[random.Next(firstNames.Length)] + suffixes[random.Next(suffixes.Length)];
                bool idle = random.NextDouble() < _options.IdleBotShare;
                double strength = Math.Pow(random.NextDouble(), Math.Max(0.01, _options.DistributionExponent));
                double startProgress = random.NextDouble() * latestStart;
                double paceExponent = Lerp(_options.BotPaceExponentMinimum, _options.BotPaceExponentMaximum, random.NextDouble());

                double seasonTrophies = idle ? 0 : (minimum + (maximum - minimum) * strength) * tierScale;
                double earnedShare = progress <= startProgress || startProgress >= 1
                    ? 0
                    : Math.Pow((progress - startProgress) / (1 - startProgress), Math.Max(0.01, paceExponent));
                long trophies = (long)Math.Floor(seasonTrophies * Clamp01(earnedShare));
                bots.Add(new SimulatedPlayer("sim-bot-" + index.ToString(CultureInfo.InvariantCulture), name, trophies, index));
            }
            return bots;
        }

        /// <summary>Cúp giảm dần; hoà thì ai "đạt trước" đứng trên — bot theo thứ tự sinh, người chơi luôn sau cùng.</summary>
        private static int ComparePlayers(SimulatedPlayer first, SimulatedPlayer second)
        {
            if (first.Trophies != second.Trophies) return second.Trophies.CompareTo(first.Trophies);
            return first.ReachedOrder.CompareTo(second.ReachedOrder);
        }

        /// <summary>FNV-1a 32 bit — không dùng string.GetHashCode vì giá trị đó không cam kết ổn định giữa runtime.</summary>
        private static int StableHash(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char character in text)
                {
                    hash ^= character;
                    hash *= 16777619;
                }
                return (int)hash;
            }
        }

        private static double Clamp01(double value)
        {
            if (value < 0) return 0;
            return value > 1 ? 1 : value;
        }

        private static double Lerp(double from, double to, double amount)
        {
            return from + (to - from) * amount;
        }

        // ---------------------------------------------------------------- Hạ tầng

        /// <summary>
        /// Đầu mọi lượt gọi: độ trễ giả lập, từ chối (lỗi tạm) lượt gọi đã bị <see cref="DebugResetSimulation"/> bỏ lại, rồi lỗi mạng giả lập.
        /// Phần còn lại của lượt gọi chạy đồng bộ sau hàm này nên lượt bị huỷ không bao giờ chạm tới dữ liệu hay nơi lưu.
        /// </summary>
        private async Task BeginCallAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int generationAtStart = Volatile.Read(ref _resetGeneration);
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL player không có thread pool: Task.Delay không đáng tin, bỏ qua độ trễ.
            await Task.CompletedTask;
#else
            if (LatencyMilliseconds > 0) await Task.Delay(LatencyMilliseconds, cancellationToken);
#endif
            if (generationAtStart != Volatile.Read(ref _resetGeneration))
            {
                // Lỗi tạm, KHÔNG phải OperationCanceledException: token của nơi gọi không bị huỷ, nên ném huỷ ở đây làm widget / host
                // hiểu nhầm là người dùng huỷ và đứng mãi ở trạng thái đang tải. Lỗi tạm thì nơi gọi báo lỗi + thử lại được.
                throw new SimulatedLeagueException("Lượt gọi bắt đầu trước khi dữ liệu mô phỏng bị xoá — bỏ, không ghi gì. Gọi lại để đọc dữ liệu mới.");
            }
            if (!_failNextCall) return;
            _failNextCall = false;
            throw new SimulatedLeagueException("Lỗi giả lập từ SimulatedLeagueGroupService.FailNextCall().");
        }

        private void Save()
        {
            _store.Write(_options.StoreKey, _data.Encode());
        }

        private sealed class SimulatedPlayer
        {
            public SimulatedPlayer(string playerId, string displayName, long trophies, int reachedOrder)
            {
                PlayerId = playerId;
                DisplayName = displayName;
                Trophies = trophies;
                ReachedOrder = reachedOrder;
            }

            public string PlayerId { get; }
            public string DisplayName { get; }
            public long Trophies { get; }
            public int ReachedOrder { get; }
        }
    }
}
