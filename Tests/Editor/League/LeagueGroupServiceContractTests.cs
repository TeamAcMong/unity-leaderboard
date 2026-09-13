using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace DreamTech.Leaderboard.League.Tests
{
    /// <summary>
    /// Hợp đồng mà MỌI bản cài <see cref="ILeagueGroupService"/> phải giữ. Viết adapter mới (backend thật...) thì tạo một lớp con
    /// cài <see cref="CreateService"/> — cùng bộ test này chạy lại, pass thì cắm vào game được.
    /// </summary>
    public abstract class LeagueGroupServiceContract
    {
        protected ManualLeagueClock Clock { get; private set; }
        protected FixedLengthSeasonSchedule Schedule { get; private set; }
        protected LeagueRules Rules { get; private set; }

        /// <summary>Tạo dịch vụ mới tinh (chưa có dữ liệu) dùng <see cref="Clock"/> và <see cref="Rules"/>.</summary>
        protected abstract ILeagueGroupService CreateService();

        /// <summary>Đặt người chơi vào hạng nhất nhóm (để test phần thưởng hạng nhất).</summary>
        protected abstract void MakeLocalPlayerFirst(ILeagueGroupService service, SeasonWindow season);

        private SeasonWindow CurrentSeason => Schedule.GetSeasonAt(Clock.UtcNow);

        [SetUp]
        public void SetUpContract()
        {
            Clock = LeagueTestFactory.CreateClockInFirstSeason();
            Schedule = LeagueTestFactory.CreateSchedule();
            Rules = LeagueTestFactory.CreateRules();
        }

        [Test]
        public void Contract_GetGroup_SortedDescending_ContiguousRanks_ContainsLocal()
        {
            ILeagueGroupService service = CreateService();
            LeagueGroupSnapshot group = service.GetGroupAsync(CurrentSeason, CancellationToken.None).Result;

            Assert.Greater(group.GroupSize, 0);
            Assert.AreEqual(CurrentSeason, group.Season);
            Assert.GreaterOrEqual(group.LocalRank, 0, "Người chơi phải có trong nhóm");
            for (int rank = 0; rank < group.GroupSize; rank++)
            {
                Assert.AreEqual(rank, group.Standings[rank].Rank);
                if (rank > 0) Assert.GreaterOrEqual(group.Standings[rank - 1].Score, group.Standings[rank].Score);
            }
        }

        [Test]
        public void Contract_AddTrophies_SameGrantTwice_CountsOnce()
        {
            ILeagueGroupService service = CreateService();
            var grant = new LeagueTrophyGrant("grant-a", CurrentSeason.SeasonId, 12);

            service.AddTrophiesAsync(CurrentSeason, grant, CancellationToken.None).Wait();
            LeagueGroupSnapshot group = service.AddTrophiesAsync(CurrentSeason, grant, CancellationToken.None).Result;

            Assert.AreEqual(12, group.LocalTrophies);
        }

        [Test]
        public void Contract_GrantOfEndedSeason_SentAfterRollover_CountsForThatSeason()
        {
            ILeagueGroupService service = CreateService();
            SeasonWindow firstSeason = CurrentSeason;
            service.AddTrophiesAsync(firstSeason, new LeagueTrophyGrant("grant-a", firstSeason.SeasonId, 5), CancellationToken.None).Wait();

            Clock.Set(firstSeason.EndUtc + TimeSpan.FromMinutes(2));
            service.AddTrophiesAsync(CurrentSeason, new LeagueTrophyGrant("grant-late", firstSeason.SeasonId, 7), CancellationToken.None).Wait();

            SeasonResult result = service.GetPendingResultAsync(CurrentSeason, CancellationToken.None).Result;
            Assert.IsNotNull(result);
            Assert.AreEqual(firstSeason.SeasonId, result.SeasonId);
            Assert.AreEqual(12, result.FinalTrophies);
        }

        [Test]
        public void Contract_ClaimSeasonReward_ReturnsPackageOnlyOnce()
        {
            ILeagueGroupService service = CreateService();
            SeasonWindow firstSeason = CurrentSeason;
            MakeLocalPlayerFirst(service, firstSeason);

            Clock.Set(firstSeason.EndUtc + TimeSpan.FromHours(1));
            SeasonResult result = service.GetPendingResultAsync(CurrentSeason, CancellationToken.None).Result;
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.FinalRank);
            Assert.AreEqual(LeagueTestFactory.GoldChest, result.Reward.ChestId);

            LeagueRewardPackage first = service.ClaimSeasonRewardAsync(result.SeasonId, CancellationToken.None).Result;
            LeagueRewardPackage second = service.ClaimSeasonRewardAsync(result.SeasonId, CancellationToken.None).Result;
            Assert.AreEqual(LeagueTestFactory.GoldChest, first.ChestId);
            Assert.IsTrue(second.IsEmpty);
        }

        [Test]
        public void Contract_ResultStaysPendingUntilAcknowledgedAndClaimed()
        {
            ILeagueGroupService service = CreateService();
            SeasonWindow firstSeason = CurrentSeason;
            MakeLocalPlayerFirst(service, firstSeason);
            Clock.Set(firstSeason.EndUtc + TimeSpan.FromHours(1));

            SeasonResult result = service.GetPendingResultAsync(CurrentSeason, CancellationToken.None).Result;
            service.AcknowledgeResultAsync(result.SeasonId, CancellationToken.None).Wait();
            SeasonResult afterAcknowledge = service.GetPendingResultAsync(CurrentSeason, CancellationToken.None).Result;
            Assert.IsNotNull(afterAcknowledge, "Đã xem kết quả nhưng chưa nhận rương thì vẫn còn việc");
            Assert.IsTrue(afterAcknowledge.Acknowledged);

            service.ClaimSeasonRewardAsync(result.SeasonId, CancellationToken.None).Wait();
            Assert.IsNull(service.GetPendingResultAsync(CurrentSeason, CancellationToken.None).Result);
        }
    }

    [TestFixture]
    public class SimulatedLeagueGroupServiceContractTests : LeagueGroupServiceContract
    {
        protected override ILeagueGroupService CreateService()
        {
            return new SimulatedLeagueGroupService(LeagueTestFactory.CreateSimulationOptions(), Rules, Clock, new InMemoryLeagueTextStore());
        }

        protected override void MakeLocalPlayerFirst(ILeagueGroupService service, SeasonWindow season)
        {
            var simulation = (SimulatedLeagueGroupService)service;
            simulation.DebugSetLocalTrophies(season, 1000000);
        }
    }

    [TestFixture]
    public class SimulatedLeagueGroupServiceTests
    {
        private ManualLeagueClock _clock;
        private FixedLengthSeasonSchedule _schedule;
        private LeagueRules _rules;

        [SetUp]
        public void SetUp()
        {
            _clock = LeagueTestFactory.CreateClockInFirstSeason();
            _schedule = LeagueTestFactory.CreateSchedule();
            _rules = LeagueTestFactory.CreateRules();
        }

        private SeasonWindow Season => _schedule.GetSeasonAt(_clock.UtcNow);

        private SimulatedLeagueGroupService Create(InMemoryLeagueTextStore store = null, SimulatedLeagueOptions options = null)
        {
            return new SimulatedLeagueGroupService(options ?? LeagueTestFactory.CreateSimulationOptions(), _rules, _clock,
                                                   store ?? new InMemoryLeagueTextStore());
        }

        [Test]
        public void SameSeedAndSeason_ProduceIdenticalStandings()
        {
            IReadOnlyList<LeaderboardEntry> first = Create().GetGroupAsync(Season, CancellationToken.None).Result.Standings;
            IReadOnlyList<LeaderboardEntry> second = Create().GetGroupAsync(Season, CancellationToken.None).Result.Standings;
            CollectionAssert.AreEqual(first.Select(entry => entry.PlayerId + ":" + entry.Score),
                                      second.Select(entry => entry.PlayerId + ":" + entry.Score));
        }

        [Test]
        public void BotTrophies_NeverDecreaseDuringSeason()
        {
            SimulatedLeagueGroupService service = Create();
            SeasonWindow season = Season;
            var previous = new Dictionary<string, long>();
            for (int step = 0; step <= 20; step++)
            {
                _clock.Set(season.StartUtc + TimeSpan.FromTicks(season.Length.Ticks * step / 20 - (step == 20 ? 1 : 0)));
                foreach (LeaderboardEntry entry in service.GetGroupAsync(season, CancellationToken.None).Result.Standings)
                {
                    if (previous.TryGetValue(entry.PlayerId, out long before)) Assert.GreaterOrEqual(entry.Score, before, entry.PlayerId);
                    previous[entry.PlayerId] = entry.Score;
                }
            }
            Assert.IsTrue(previous.Values.Any(score => score > 0), "Cuối mùa phải có bot kiếm được cúp");
        }

        [Test]
        public void State_SurvivesNewInstanceOnSameStore()
        {
            var store = new InMemoryLeagueTextStore();
            SimulatedLeagueGroupService first = Create(store);
            first.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-a", Season.SeasonId, 33), CancellationToken.None).Wait();
            first.DebugSetTierIndex(Season, 2);

            SimulatedLeagueGroupService reopened = Create(store);
            LeagueGroupSnapshot group = reopened.GetGroupAsync(Season, CancellationToken.None).Result;
            Assert.AreEqual(33, group.LocalTrophies);
            Assert.AreEqual(2, group.TierIndex);

            reopened.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-a", Season.SeasonId, 33), CancellationToken.None).Wait();
            Assert.AreEqual(33, reopened.LocalTrophies, "Grant đã nhận trước khi mở lại app không được cộng lần nữa");
        }

        [Test]
        public void SeasonEnd_FirstPlace_PromotesAndNextSeasonUsesNewTier()
        {
            SimulatedLeagueGroupService service = Create();
            service.DebugSetLocalTrophies(Season, 1000000);
            SeasonWindow firstSeason = Season;

            _clock.Set(firstSeason.EndUtc + TimeSpan.FromHours(1));
            SeasonResult result = service.GetPendingResultAsync(Season, CancellationToken.None).Result;
            Assert.AreEqual(SeasonOutcome.Promoted, result.Outcome);
            Assert.AreEqual(0, result.TierIndexBefore);
            Assert.AreEqual(1, result.TierIndexAfter);

            LeagueGroupSnapshot nextGroup = service.GetGroupAsync(Season, CancellationToken.None).Result;
            Assert.AreEqual(1, nextGroup.TierIndex);
            Assert.AreEqual(0, nextGroup.LocalTrophies);
        }

        [Test]
        public void SeasonEnd_LastPlaceInSilver_Demotes()
        {
            SimulatedLeagueOptions options = LeagueTestFactory.CreateSimulationOptions();
            options.IdleBotShare = 0;
            options.LatestBotStartProgress = 0;
            SimulatedLeagueGroupService service = Create(options: options);
            service.DebugSetTierIndex(Season, 1);
            service.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-a", Season.SeasonId, 1), CancellationToken.None).Wait();

            _clock.Set(Season.EndUtc + TimeSpan.FromHours(1));
            SeasonResult result = service.GetPendingResultAsync(Season, CancellationToken.None).Result;
            Assert.AreEqual(29, result.FinalRank);
            Assert.AreEqual(SeasonOutcome.Demoted, result.Outcome);
            Assert.AreEqual(0, result.TierIndexAfter);
            Assert.AreEqual(LeagueTestFactory.RegularChest, result.Reward.ChestId);
        }

        [Test]
        public void SeasonWithoutTrophies_LeavesNoResult()
        {
            SimulatedLeagueGroupService service = Create();
            service.GetGroupAsync(Season, CancellationToken.None).Wait();
            _clock.Set(Season.EndUtc + TimeSpan.FromHours(1));
            Assert.IsNull(service.GetPendingResultAsync(Season, CancellationToken.None).Result);
        }

        [Test]
        public void TrophiesToReachRank_PlacesLocalPlayerAtThatRank()
        {
            SimulatedLeagueGroupService service = Create();
            _clock.Set(Season.StartUtc + TimeSpan.FromDays(5));

            // Hạng r chỉ đạt đúng khi bot ở r-1 nhiều cúp hơn bot ở r (hoà cúp thì người chơi đứng sau) — chọn hạng không bị hoà.
            IReadOnlyList<LeaderboardEntry> standings = service.GetGroupAsync(Season, CancellationToken.None).Result.Standings;
            int targetRank = Enumerable.Range(1, 10).First(rank => standings[rank - 1].Score > standings[rank].Score);

            long trophies = service.DebugTrophiesToReachRank(Season, targetRank);
            service.DebugSetLocalTrophies(Season, trophies);
            Assert.AreEqual(targetRank, service.GetGroupAsync(Season, CancellationToken.None).Result.LocalRank);
        }

        [Test]
        public void FailNextCall_ThrowsOnce()
        {
            SimulatedLeagueGroupService service = Create();
            service.FailNextCall();
            var failure = Assert.Throws<AggregateException>(() => service.GetGroupAsync(Season, CancellationToken.None).Wait());
            Assert.IsInstanceOf<SimulatedLeagueException>(failure.InnerException);
            Assert.DoesNotThrow(() => service.GetGroupAsync(Season, CancellationToken.None).Wait());
        }

        [Test]
        public void CorruptStore_StartsFresh()
        {
            var store = new InMemoryLeagueTextStore();
            store.Write(LeagueTestFactory.CreateSimulationOptions().StoreKey, "not a record");
            LeagueGroupSnapshot group = Create(store).GetGroupAsync(Season, CancellationToken.None).Result;
            Assert.AreEqual(0, group.TierIndex);
            Assert.AreEqual(0, group.LocalTrophies);
        }
    }
}
