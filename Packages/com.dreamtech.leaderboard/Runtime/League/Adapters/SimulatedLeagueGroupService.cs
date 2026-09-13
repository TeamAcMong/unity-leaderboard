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

        /// <summary>Số kết quả mùa giữ lại tối đa (kết quả còn việc cho UI được ưu tiên giữ).</summary>
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

    /// <summary>Lỗi giả lập do <see cref="SimulatedLeagueGroupService.FailNextCall"/>.</summary>
    public sealed class SimulatedLeagueException : Exception
    {
        public SimulatedLeagueException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Dịch vụ nhóm chạy offline: nhóm gồm người chơi + bot. Cúp của bot là hàm tất định của (seed, mùa, tier, người chơi, thời
    /// điểm) — không lưu bot, mở lại app hay cài lại vẫn ra cùng bảng, và cúp bot chỉ tăng theo thời gian. Máy chỉ lưu bậc hiện
    /// tại, cúp của người chơi, các grant đã nhận và kết quả mùa.
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

        // ---------------------------------------------------------------- ILeagueGroupService

        public async Task<LeagueGroupSnapshot> GetGroupAsync(SeasonWindow currentSeason, CancellationToken cancellationToken)
        {
            if (currentSeason == null) throw new ArgumentNullException(nameof(currentSeason));
            await BeginCallAsync(cancellationToken);
            EnsureActiveSeason(currentSeason);
            return BuildSnapshot(_clock.UtcNow);
        }

        public async Task<LeagueGroupSnapshot> AddTrophiesAsync(SeasonWindow currentSeason, LeagueTrophyGrant grant, CancellationToken cancellationToken)
        {
            if (currentSeason == null) throw new ArgumentNullException(nameof(currentSeason));
            await BeginCallAsync(cancellationToken);

            // Grant của mùa đang chạy được cộng TRƯỚC khi khép mùa: thắng lúc 23:59 gửi lúc 00:01 vẫn tính cho mùa cũ.
            if (_data.HasActiveSeason && string.Equals(grant.SeasonId, _data.ActiveSeasonId, StringComparison.Ordinal) &&
                !_data.AppliedGrantIds.Contains(grant.GrantId))
            {
                _data.LocalTrophies += grant.Trophies;
                _data.AppliedGrantIds.Add(grant.GrantId);
            }
            else if (!_data.HasActiveSeason && string.Equals(grant.SeasonId, currentSeason.SeasonId, StringComparison.Ordinal))
            {
                StartActiveSeason(currentSeason);
                _data.LocalTrophies += grant.Trophies;
                _data.AppliedGrantIds.Add(grant.GrantId);
            }

            EnsureActiveSeason(currentSeason);
            Save();
            return BuildSnapshot(_clock.UtcNow);
        }

        public async Task<SeasonResult> GetPendingResultAsync(SeasonWindow currentSeason, CancellationToken cancellationToken)
        {
            if (currentSeason == null) throw new ArgumentNullException(nameof(currentSeason));
            await BeginCallAsync(cancellationToken);
            EnsureActiveSeason(currentSeason);
            foreach (SeasonResult result in _data.Results)
            {
                if (result.IsPending) return result;
            }
            return null;
        }

        public async Task AcknowledgeResultAsync(string seasonId, CancellationToken cancellationToken)
        {
            await BeginCallAsync(cancellationToken);
            int index = IndexOfResult(seasonId);
            if (index < 0 || _data.Results[index].Acknowledged) return;
            _data.Results[index] = _data.Results[index].WithAcknowledged();
            TrimResults();
            Save();
        }

        public async Task<LeagueRewardPackage> ClaimSeasonRewardAsync(string seasonId, CancellationToken cancellationToken)
        {
            await BeginCallAsync(cancellationToken);
            int index = IndexOfResult(seasonId);
            if (index < 0 || !_data.Results[index].HasUnclaimedReward) return LeagueRewardPackage.None;
            SeasonResult result = _data.Results[index];
            _data.Results[index] = result.WithRewardClaimed();
            TrimResults();
            Save();
            return result.Reward;
        }

        // ---------------------------------------------------------------- Công cụ debug / cheat

        /// <summary>Lần gọi kế tiếp ném <see cref="SimulatedLeagueException"/>.</summary>
        public void FailNextCall()
        {
            _failNextCall = true;
        }

        /// <summary>Đặt cúp người chơi của mùa <paramref name="currentSeason"/> (tự vào nhóm nếu chưa).</summary>
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
        /// true = tính theo bảng lúc mùa kết thúc, tức là đủ để GIỮ hạng đó tới cuối mùa.
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

        /// <summary>Xoá toàn bộ dữ liệu mô phỏng (bậc, cúp, kết quả).</summary>
        public void DebugResetSimulation()
        {
            _store.Delete(_options.StoreKey);
            _data.ClearActiveSeason();
            _data.Results.Clear();
            _data.TierIndex = _rules.Ladder.ClampIndex(_options.StartingTierIndex);
        }

        // ---------------------------------------------------------------- Mùa

        private void EnsureActiveSeason(SeasonWindow currentSeason)
        {
            if (!_data.HasActiveSeason)
            {
                StartActiveSeason(currentSeason);
                Save();
                return;
            }
            if (string.Equals(_data.ActiveSeasonId, currentSeason.SeasonId, StringComparison.Ordinal)) return;

            // Mùa mới bắt đầu sau khi mùa đang giữ đã hết → khép mùa cũ. Ngược lại là giờ bị lùi / lịch đổi → bỏ mùa đang giữ.
            if (currentSeason.StartUtc >= _data.ActiveSeasonEnd) FinalizeActiveSeason();
            StartActiveSeason(currentSeason);
            Save();
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
            SeasonWindow window = ActiveWindow();
            LeagueGroupSnapshot finalStandings = BuildSnapshot(window.EndUtc);
            bool participated = _data.LocalTrophies > 0;
            var input = new SeasonOutcomeInput(_data.ActiveTierIndex, finalStandings.LocalRank, finalStandings.GroupSize,
                                               _data.LocalTrophies, participated);
            SeasonOutcomeDecision decision = _rules.Decide(input);
            LeagueRewardPackage reward = participated
                ? _rules.RewardFor(_data.ActiveTierIndex, finalStandings.LocalRank, finalStandings.GroupSize)
                : LeagueRewardPackage.None;

            _data.TierIndex = decision.NextTierIndex;
            if (participated || decision.Outcome != SeasonOutcome.Unchanged)
            {
                _data.Results.Add(new SeasonResult(window.SeasonId, _data.ActiveTierIndex, decision.NextTierIndex, decision.Outcome,
                                                   finalStandings.LocalRank, finalStandings.GroupSize, _data.LocalTrophies, reward,
                                                   acknowledged: false, rewardClaimed: false));
                TrimResults();
            }
            _data.ClearActiveSeason();
        }

        private void TrimResults()
        {
            int maximum = Math.Max(1, _options.MaximumStoredResults);
            for (int index = 0; index < _data.Results.Count && _data.Results.Count > maximum;)
            {
                if (!_data.Results[index].IsPending) _data.Results.RemoveAt(index);
                else index++;
            }
            while (_data.Results.Count > maximum) _data.Results.RemoveAt(0);
        }

        private int IndexOfResult(string seasonId)
        {
            for (int index = 0; index < _data.Results.Count; index++)
            {
                if (string.Equals(_data.Results[index].SeasonId, seasonId, StringComparison.Ordinal)) return index;
            }
            return -1;
        }

        private SeasonWindow ActiveWindow()
        {
            return new SeasonWindow(_data.ActiveSeasonId, _data.ActiveSeasonStart, _data.ActiveSeasonEnd);
        }

        // ---------------------------------------------------------------- Bảng xếp hạng

        private LeagueGroupSnapshot BuildSnapshot(DateTime atUtc)
        {
            SeasonWindow window = ActiveWindow();
            List<SimulatedPlayer> players = BuildBots(window, _data.ActiveTierIndex, atUtc);
            players.Add(new SimulatedPlayer(_options.LocalPlayerId, _options.LocalDisplayName, _data.LocalTrophies, int.MaxValue));
            players.Sort(ComparePlayers);

            var standings = new List<LeaderboardEntry>(players.Count);
            for (int rank = 0; rank < players.Count; rank++)
            {
                SimulatedPlayer player = players[rank];
                standings.Add(new LeaderboardEntry(player.PlayerId, player.DisplayName, player.Trophies, rank));
            }
            return new LeagueGroupSnapshot(window, _data.ActiveTierIndex, standings, _options.LocalPlayerId);
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

        private async Task BeginCallAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL player không có thread pool: Task.Delay không đáng tin, bỏ qua độ trễ.
            await Task.CompletedTask;
#else
            if (LatencyMilliseconds > 0) await Task.Delay(LatencyMilliseconds, cancellationToken);
#endif
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

    /// <summary>Phần dữ liệu mô phỏng lưu trên máy.</summary>
    internal sealed class SimulatedLeagueData
    {
        private const int CurrentFormat = 1;

        public int TierIndex;
        public string ActiveSeasonId;
        public DateTime ActiveSeasonStart;
        public DateTime ActiveSeasonEnd;
        public int ActiveTierIndex;
        public long LocalTrophies;
        public readonly HashSet<string> AppliedGrantIds = new HashSet<string>(StringComparer.Ordinal);
        public readonly List<SeasonResult> Results = new List<SeasonResult>();

        public bool HasActiveSeason => !string.IsNullOrEmpty(ActiveSeasonId);

        public void ClearActiveSeason()
        {
            ActiveSeasonId = null;
            ActiveSeasonStart = default;
            ActiveSeasonEnd = default;
            ActiveTierIndex = 0;
            LocalTrophies = 0;
            AppliedGrantIds.Clear();
        }

        public string Encode()
        {
            var record = new LeagueTextRecord(CurrentFormat);
            record.SetInt("tier", TierIndex);
            if (HasActiveSeason)
            {
                record.SetString("active.season", ActiveSeasonId);
                record.SetLong("active.start", ActiveSeasonStart.Ticks);
                record.SetLong("active.end", ActiveSeasonEnd.Ticks);
                record.SetInt("active.tier", ActiveTierIndex);
                record.SetLong("active.trophies", LocalTrophies);
                var grantIds = new List<string>(AppliedGrantIds);
                grantIds.Sort(StringComparer.Ordinal);
                record.SetInt("active.grants", grantIds.Count);
                for (int index = 0; index < grantIds.Count; index++)
                {
                    record.SetString("active.grant" + index.ToString(CultureInfo.InvariantCulture), grantIds[index]);
                }
            }
            record.SetInt("results", Results.Count);
            for (int index = 0; index < Results.Count; index++)
            {
                record.SetResult("result" + index.ToString(CultureInfo.InvariantCulture), Results[index]);
            }
            return record.Encode();
        }

        /// <summary>Chuỗi hỏng hoặc khác định dạng → null (bắt đầu lại từ đầu).</summary>
        public static SimulatedLeagueData Decode(string text)
        {
            if (!LeagueTextRecord.TryDecode(text, out LeagueTextRecord record) || record.Format != CurrentFormat) return null;
            var data = new SimulatedLeagueData { TierIndex = record.GetInt("tier", 0) };

            string activeSeasonId = record.GetString("active.season", string.Empty);
            long startTicks = record.GetLong("active.start", 0);
            long endTicks = record.GetLong("active.end", 0);
            if (activeSeasonId.Length > 0 && endTicks > startTicks && startTicks >= DateTime.MinValue.Ticks && endTicks <= DateTime.MaxValue.Ticks)
            {
                data.ActiveSeasonId = activeSeasonId;
                data.ActiveSeasonStart = new DateTime(startTicks, DateTimeKind.Utc);
                data.ActiveSeasonEnd = new DateTime(endTicks, DateTimeKind.Utc);
                data.ActiveTierIndex = record.GetInt("active.tier", data.TierIndex);
                data.LocalTrophies = Math.Max(0, record.GetLong("active.trophies", 0));
                int grantCount = record.GetInt("active.grants", 0);
                for (int index = 0; index < grantCount; index++)
                {
                    string grantId = record.GetString("active.grant" + index.ToString(CultureInfo.InvariantCulture), string.Empty);
                    if (grantId.Length > 0) data.AppliedGrantIds.Add(grantId);
                }
            }

            int resultCount = record.GetInt("results", 0);
            for (int index = 0; index < resultCount; index++)
            {
                SeasonResult result = record.GetResult("result" + index.ToString(CultureInfo.InvariantCulture));
                if (result != null) data.Results.Add(result);
            }
            return data;
        }
    }
}
