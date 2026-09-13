using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DreamTech.Leaderboard.League.Tests
{
    /// <summary>Dựng bộ luật và hệ thống League giống design (5 tier, streak 5 bậc) để các test dùng chung.</summary>
    internal static class LeagueTestFactory
    {
        /// <summary>Thứ Hai 2026-01-05 00:00 UTC.</summary>
        public static readonly DateTime Anchor = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        public static readonly TimeSpan SeasonLength = TimeSpan.FromDays(7);

        public const string RegularChest = "chest.regular";
        public const string GoldChest = "chest.gold";

        public static LeagueLadder CreateLadder()
        {
            return new LeagueLadder(new[]
            {
                new LeagueTierDefinition("bronze", 5, 5),
                new LeagueTierDefinition("silver", 5, 5),
                new LeagueTierDefinition("gold", 5, 5),
                new LeagueTierDefinition("platinum", 5, 5),
                new LeagueTierDefinition("diamond", 5, 5),
            });
        }

        public static WinStreakLadder CreateStreakLadder(LeagueRewardPackage levelTwoReward = null)
        {
            return new WinStreakLadder(new[]
            {
                new WinStreakStep(1),
                new WinStreakStep(2, levelTwoReward),
                new WinStreakStep(3),
                new WinStreakStep(4),
                new WinStreakStep(5),
            });
        }

        public static RankBracketRewardTable CreateRewardTable()
        {
            return new RankBracketRewardTable(new[]
            {
                new LeagueRewardBracket(LeagueRewardBracket.AnyTier, 0, 0,
                                        new LeagueRewardPackage(GoldChest, new[] { new LeagueRewardItem("coin", 100) })),
                new LeagueRewardBracket(LeagueRewardBracket.AnyTier, 1, LeagueRewardBracket.ToLastRank,
                                        new LeagueRewardPackage(RegularChest, new[] { new LeagueRewardItem("coin", 10) })),
            });
        }

        public static LeagueRules CreateRules(ISeasonOutcomeRule outcomeRule = null)
        {
            return new LeagueRules(CreateLadder(), outcomeRule: outcomeRule, rewardTable: CreateRewardTable());
        }

        public static FixedLengthSeasonSchedule CreateSchedule()
        {
            return new FixedLengthSeasonSchedule(Anchor, SeasonLength);
        }

        public static ManualLeagueClock CreateClockInFirstSeason()
        {
            return new ManualLeagueClock(Anchor + TimeSpan.FromDays(1));
        }

        public static SimulatedLeagueOptions CreateSimulationOptions()
        {
            return new SimulatedLeagueOptions
            {
                GroupSize = 30,
                Seed = 5,
                LatencyMilliseconds = 0,
            };
        }
    }

    /// <summary>Một lần dựng đầy đủ: đồng hồ, lưu trữ, dịch vụ mô phỏng (có đếm lượt gọi), granter ghi lại.</summary>
    internal sealed class LeagueScenario
    {
        public const string SystemId = "test-league";

        public ManualLeagueClock Clock;
        public FixedLengthSeasonSchedule Schedule;
        public InMemoryLeagueTextStore Store;
        public LeagueRules Rules;
        public WinStreakLadder StreakLadder;
        public SimulatedLeagueGroupService Simulation;
        public CountingLeagueGroupService Service;
        public RecordingLeagueRewardGranter Granter;
        public ManualLeagueFeatureGate Gate;
        public LeagueSystem System;

        public static LeagueScenario Create(SimulatedLeagueOptions options = null, LeagueRewardPackage streakLevelTwoReward = null,
                                            InMemoryLeagueTextStore store = null, ManualLeagueClock clock = null)
        {
            var scenario = new LeagueScenario
            {
                Clock = clock ?? LeagueTestFactory.CreateClockInFirstSeason(),
                Schedule = LeagueTestFactory.CreateSchedule(),
                Store = store ?? new InMemoryLeagueTextStore(),
                Rules = LeagueTestFactory.CreateRules(),
                StreakLadder = LeagueTestFactory.CreateStreakLadder(streakLevelTwoReward),
                Granter = new RecordingLeagueRewardGranter(),
                Gate = new ManualLeagueFeatureGate(true),
            };
            scenario.Simulation = new SimulatedLeagueGroupService(options ?? LeagueTestFactory.CreateSimulationOptions(), scenario.Rules,
                                                                  scenario.Clock, scenario.Store);
            scenario.Service = new CountingLeagueGroupService(scenario.Simulation);
            scenario.System = scenario.Rebuild();
            return scenario;
        }

        /// <summary>Dựng lại LeagueSystem trên cùng nơi lưu (giả lập mở lại app).</summary>
        public LeagueSystem Rebuild()
        {
            System = new LeagueSystemBuilder(SystemId, Rules, StreakLadder)
                     .WithGroupService(Service)
                     .WithSchedule(Schedule)
                     .WithClock(Clock)
                     .WithTextStore(Store)
                     .WithRewardGranter(Granter)
                     .WithFeatureGate(Gate)
                     .WithTrophyRule(new MultipliedTrophyRule(new[] { 10, 15, 20 }))
                     .Build();
            return System;
        }

        public void AdvancePastSeasonEnd()
        {
            SeasonWindow season = Schedule.GetSeasonAt(Clock.UtcNow);
            Clock.Set(season.EndUtc + TimeSpan.FromHours(1));
        }
    }

    internal sealed class RecordingLeagueRewardGranter : ILeagueRewardGranter
    {
        public readonly List<string> GrantedIds = new List<string>();
        public readonly List<LeagueRewardPackage> GrantedPackages = new List<LeagueRewardPackage>();
        public bool Ready = true;

        public bool TryGrant(string grantId, LeagueRewardPackage package)
        {
            if (!Ready) return false;
            GrantedIds.Add(grantId);
            GrantedPackages.Add(package);
            return true;
        }
    }

    /// <summary>Decorator đếm lượt gọi — kiểm hệ thống không gửi thừa.</summary>
    internal sealed class CountingLeagueGroupService : ILeagueGroupService
    {
        private readonly ILeagueGroupService _inner;

        public CountingLeagueGroupService(ILeagueGroupService inner)
        {
            _inner = inner;
        }

        public int AddTrophiesCallCount { get; private set; }
        public int GetGroupCallCount { get; private set; }
        public string LocalPlayerId => _inner.LocalPlayerId;

        public Task<LeagueGroupSnapshot> GetGroupAsync(SeasonWindow currentSeason, CancellationToken cancellationToken)
        {
            GetGroupCallCount++;
            return _inner.GetGroupAsync(currentSeason, cancellationToken);
        }

        public Task<LeagueGroupSnapshot> AddTrophiesAsync(SeasonWindow currentSeason, LeagueTrophyGrant grant, CancellationToken cancellationToken)
        {
            AddTrophiesCallCount++;
            return _inner.AddTrophiesAsync(currentSeason, grant, cancellationToken);
        }

        public Task<SeasonResult> GetPendingResultAsync(SeasonWindow currentSeason, CancellationToken cancellationToken)
        {
            return _inner.GetPendingResultAsync(currentSeason, cancellationToken);
        }

        public Task AcknowledgeResultAsync(string seasonId, CancellationToken cancellationToken)
        {
            return _inner.AcknowledgeResultAsync(seasonId, cancellationToken);
        }

        public Task<LeagueRewardPackage> ClaimSeasonRewardAsync(string seasonId, CancellationToken cancellationToken)
        {
            return _inner.ClaimSeasonRewardAsync(seasonId, cancellationToken);
        }
    }
}
