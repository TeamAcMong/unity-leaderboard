using System;
using System.Collections.Generic;
using DreamTech.Leaderboard.League;
using DreamTech.Leaderboard.League.Unity;
using UnityEngine;

namespace DreamTech.Leaderboard.Demo
{
    /// <summary>
    /// Bàn thử League (chỉ nằm trong dev project, không đi theo package).
    ///
    /// <para>Lắp đúng những khối mà game sẽ lắp — dịch vụ nhóm mô phỏng, lịch mùa, đồng hồ tua được, PlayerPrefs, granter giả
    /// lập kho đồ — rồi giao phần bày nút và vẽ bảng cho <see cref="LeagueDebugPanel"/> của package (game thật dùng lại đúng
    /// panel đó). Mục đích là thử LUẬT và LUỒNG khi chưa có art.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LeagueDemo : MonoBehaviour
    {
        private const string SystemId = "demo-league";
        private const string StoreKeyPrefix = "dreamtech.league.demo.";

        [Header("Nhóm")]
        [SerializeField, Min(2)] private int groupSize = 30;
        [SerializeField] private int simulationSeed = 5;
        [SerializeField, Min(0)] private int startingTierIndex = 0;
        [Tooltip("Độ trễ mạng giả lập mỗi lần gọi (ms).")]
        [SerializeField, Min(0)] private int latencyMilliseconds = 120;

        [Header("Mùa")]
        [Tooltip("Mùa dài bao nhiêu giờ. Design dùng theo tuần (168h); để ngắn cho dễ thử.")]
        [SerializeField, Min(0.05f)] private float seasonLengthHours = 24f;
        [SerializeField, Min(1)] private int promotionCount = 5;
        [SerializeField, Min(1)] private int demotionCount = 5;

        [Header("Cúp và streak")]
        [Tooltip("Cúp gốc theo độ khó: thường / khó / siêu khó.")]
        [SerializeField] private int[] trophiesByDifficulty = { 10, 15, 20 };
        [Tooltip("Hệ số cúp của từng bậc streak (kệ 5 cúp trong design = 5 bậc).")]
        [SerializeField] private int[] streakMultipliers = { 1, 2, 3, 4, 5 };

        private LeagueSystem _league;
        private SimulatedLeagueGroupService _simulation;
        private OffsetLeagueClock _clock;
        private ManualLeagueFeatureGate _featureGate;
        private LeagueDebugPanel _panel;
        private readonly Dictionary<string, int> _wallet = new Dictionary<string, int>(StringComparer.Ordinal);

        public LeagueSystem League => _league;
        public SimulatedLeagueGroupService Simulation => _simulation;
        public OffsetLeagueClock Clock => _clock;
        public LeagueDebugPanel Panel => _panel;
        public LeaguePageData Page => _panel != null ? _panel.Page : null;
        public SeasonResult PendingResult => _panel != null ? _panel.PendingResult : null;
        public IReadOnlyDictionary<string, int> Wallet => _wallet;

        /// <summary>Đang có lệnh gọi dịch vụ chạy dở — test chờ trạng thái yên trước khi đọc bảng.</summary>
        public bool IsBusy => _panel != null && _panel.IsBusy;

        private void Awake()
        {
            Build();
            _panel = gameObject.AddComponent<LeagueDebugPanel>();
            _panel.ExtraStatusLine = FormatWallet;
            _panel.Bind(_league, _simulation, _clock, _featureGate);
        }

        private void OnDestroy()
        {
            if (_league != null) LeagueSystemRegistry.Unregister(_league);
        }

        // ---------------------------------------------------------------- Lắp ráp

        private void Build()
        {
            var ladder = new LeagueLadder(new[]
            {
                new LeagueTierDefinition("Bronze", promotionCount, demotionCount),
                new LeagueTierDefinition("Silver", promotionCount, demotionCount),
                new LeagueTierDefinition("Gold", promotionCount, demotionCount),
                new LeagueTierDefinition("Platinum", promotionCount, demotionCount),
                new LeagueTierDefinition("Diamond", promotionCount, demotionCount),
            });

            var rewardTable = new RankBracketRewardTable(new[]
            {
                new LeagueRewardBracket(LeagueRewardBracket.AnyTier, 0, 0,
                                        new LeagueRewardPackage("chest.gold", new[] { new LeagueRewardItem("coin", 100), new LeagueRewardItem("booster.wiper", 1) })),
                new LeagueRewardBracket(LeagueRewardBracket.AnyTier, 1, 1,
                                        new LeagueRewardPackage("chest.silver", new[] { new LeagueRewardItem("coin", 60) })),
                new LeagueRewardBracket(LeagueRewardBracket.AnyTier, 2, 2,
                                        new LeagueRewardPackage("chest.bronze", new[] { new LeagueRewardItem("coin", 40) })),
                new LeagueRewardBracket(LeagueRewardBracket.AnyTier, 3, LeagueRewardBracket.ToLastRank,
                                        new LeagueRewardPackage("chest.regular", new[] { new LeagueRewardItem("coin", 10) })),
            });

            var streakSteps = new List<WinStreakStep>();
            for (int index = 0; index < streakMultipliers.Length; index++)
            {
                // Bậc 3 tặng quà để thấy đường "quà streak" chạy qua granter.
                LeagueRewardPackage reward = index == 2
                    ? new LeagueRewardPackage(string.Empty, new[] { new LeagueRewardItem("booster.magnet", 1) })
                    : null;
                streakSteps.Add(new WinStreakStep(Mathf.Max(1, streakMultipliers[index]), reward));
            }

            var rules = new LeagueRules(ladder, rewardTable: rewardTable);
            _clock = new OffsetLeagueClock(new SystemLeagueClock());
            var store = new PlayerPrefsLeagueTextStore(StoreKeyPrefix);
            _featureGate = new ManualLeagueFeatureGate(true);

            var options = new SimulatedLeagueOptions
            {
                LocalPlayerId = "you",
                LocalDisplayName = "You",
                GroupSize = groupSize,
                Seed = simulationSeed,
                StartingTierIndex = startingTierIndex,
                LatencyMilliseconds = latencyMilliseconds,
                StoreKey = "simulation",
            };
            _simulation = new SimulatedLeagueGroupService(options, rules, _clock, store);

            // Mốc mùa cố định để id mùa không đổi giữa các lần chạy.
            var anchor = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
            _league = new LeagueSystemBuilder(SystemId, rules, new WinStreakLadder(streakSteps))
                      .WithGroupService(_simulation)
                      .WithSchedule(new FixedLengthSeasonSchedule(anchor, TimeSpan.FromHours(seasonLengthHours)))
                      .WithClock(_clock)
                      .WithTextStore(store)
                      .WithRewardGranter(new DelegateLeagueRewardGranter(GrantToWallet))
                      .WithFeatureGate(_featureGate)
                      .WithTrophyRule(new MultipliedTrophyRule(trophiesByDifficulty))
                      .WithWinStreakRule(new StandardWinStreakRule())
                      .Build();
            LeagueSystemRegistry.Register(_league);
        }

        private bool GrantToWallet(string grantId, LeagueRewardPackage package)
        {
            foreach (LeagueRewardItem item in package.Items)
            {
                _wallet.TryGetValue(item.ItemId, out int amount);
                _wallet[item.ItemId] = amount + item.Amount;
            }
            if (_panel != null) _panel.Log("Nhận quà: " + package);
            return true;
        }

        private string FormatWallet()
        {
            if (_wallet.Count == 0) return "Ví: trống";
            var parts = new List<string>(_wallet.Count);
            foreach (KeyValuePair<string, int> entry in _wallet) parts.Add(entry.Key + " x" + entry.Value);
            return "Ví: " + string.Join(", ", parts);
        }

        // ---------------------------------------------------------------- Chuyển tiếp cho test PlayMode

        public void WinLevel(int difficultyIndex)
        {
            _panel.WinLevel(difficultyIndex);
        }

        public void RaiseStreakEvent(WinStreakEvent streakEvent)
        {
            _panel.RaiseStreakEvent(streakEvent);
        }

        public void AdvanceToSeasonEnd()
        {
            _panel.AdvanceToSeasonEnd();
        }

        public void ClimbToRank(int oneBasedRank, bool holdUntilSeasonEnd = false)
        {
            _panel.ClimbToRank(oneBasedRank, holdUntilSeasonEnd);
        }

        public void Reload()
        {
            _panel.Reload();
        }

        public void AcknowledgeResult()
        {
            _panel.AcknowledgeResult();
        }

        public void ClaimReward()
        {
            _panel.ClaimReward();
        }

        public void FailNextCall()
        {
            _panel.FailNextCall();
        }

        public void ToggleFeatureGate()
        {
            _panel.ToggleFeatureGate();
        }

        public void ResetEverything()
        {
            _wallet.Clear();
            _panel.ResetEverything();
        }
    }
}
