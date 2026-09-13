using System;
using System.Threading;
using NUnit.Framework;

namespace DreamTech.Leaderboard.League.Tests
{
    [TestFixture]
    public class LeagueSystemTests
    {
        private static readonly LevelWinContext NormalWin = new LevelWinContext(12, 0);

        [Test]
        public void Build_WithoutRequiredModules_Throws()
        {
            var builder = new LeagueSystemBuilder("x", LeagueTestFactory.CreateRules(), LeagueTestFactory.CreateStreakLadder());
            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Test]
        public void RecordLevelWin_QueuesTrophiesWithMultiplierOfStreakCarriedIn()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            LeagueSystem system = scenario.System;

            LevelWinOutcome first = system.RecordLevelWin(NormalWin);
            LevelWinOutcome second = system.RecordLevelWin(NormalWin);
            LevelWinOutcome third = system.RecordLevelWin(NormalWin);

            Assert.AreEqual(10, first.Trophies, "L0 → x1");
            Assert.AreEqual(10, second.Trophies, "L1 → bậc 1 = x1");
            Assert.AreEqual(20, third.Trophies, "L2 → bậc 2 = x2");
            Assert.AreEqual(3, system.Streak.Level);
            Assert.AreEqual(3, system.PendingTrophyGrantCount);
            Assert.AreEqual(40, system.UnsentTrophies);
            Assert.AreEqual(0, scenario.Service.AddTrophiesCallCount, "Thắng level không được chờ mạng");
        }

        [Test]
        public void Locked_DoesNotAwardOrTouchStreak()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            scenario.Gate.IsUnlocked = false;

            LevelWinOutcome outcome = scenario.System.RecordLevelWin(NormalWin);
            WinStreakChange quit = scenario.System.RecordStreakEvent(WinStreakEvent.LevelQuit);

            Assert.IsFalse(outcome.Awarded);
            Assert.AreEqual(0, scenario.System.PendingTrophyGrantCount);
            Assert.IsFalse(quit.Changed);
            Assert.IsFalse(scenario.System.WouldLoseStreak(WinStreakEvent.LevelQuit));
        }

        [Test]
        public void Quit_LosesStreak_Revive_KeepsIt()
        {
            LeagueSystem system = LeagueScenario.Create().System;
            system.RecordLevelWin(NormalWin);
            system.RecordLevelWin(NormalWin);

            Assert.IsFalse(system.WouldLoseStreak(WinStreakEvent.LevelRevived));
            Assert.IsFalse(system.RecordStreakEvent(WinStreakEvent.LevelRevived).Changed);

            Assert.IsTrue(system.WouldLoseStreak(WinStreakEvent.LevelQuit));
            WinStreakChange quit = system.RecordStreakEvent(WinStreakEvent.LevelQuit);
            Assert.IsTrue(quit.WasLost);
            Assert.IsTrue(system.Streak.IsEmpty);
            Assert.IsFalse(system.WouldLoseStreak(WinStreakEvent.LevelQuit), "Không còn streak thì không cần cảnh báo");
        }

        [Test]
        public void RecordStreakEvent_Won_Throws()
        {
            LeagueSystem system = LeagueScenario.Create().System;
            Assert.Throws<ArgumentException>(() => system.RecordStreakEvent(WinStreakEvent.LevelWon));
        }

        [Test]
        public void SwappedStreakRule_ChangesBehaviourWithoutTouchingSystem()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            LeagueSystem strict = new LeagueSystemBuilder("strict", scenario.Rules, scenario.StreakLadder)
                                  .WithGroupService(scenario.Service)
                                  .WithSchedule(scenario.Schedule)
                                  .WithClock(scenario.Clock)
                                  .WithTrophyRule(new MultipliedTrophyRule(new[] { 10 }))
                                  .WithWinStreakRule(new StandardWinStreakRule(reviveKeepsStreak: false))
                                  .Build();
            strict.RecordLevelWin(NormalWin);

            Assert.IsTrue(strict.WouldLoseStreak(WinStreakEvent.LevelRevived));
            Assert.IsTrue(strict.RecordStreakEvent(WinStreakEvent.LevelRevived).WasLost);
        }

        [Test]
        public void Flush_SendsEachGrantOnce()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            scenario.System.RecordLevelWin(NormalWin);
            scenario.System.RecordLevelWin(NormalWin);

            Assert.AreEqual(2, scenario.System.FlushPendingTrophiesAsync(CancellationToken.None).Result);
            Assert.AreEqual(0, scenario.System.FlushPendingTrophiesAsync(CancellationToken.None).Result);
            Assert.AreEqual(2, scenario.Service.AddTrophiesCallCount);
            Assert.AreEqual(20, scenario.Simulation.LocalTrophies);
        }

        [Test]
        public void Flush_Failure_KeepsGrant_NextFlushDelivers()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            scenario.System.RecordLevelWin(NormalWin);
            scenario.Simulation.FailNextCall();

            Assert.Throws<AggregateException>(() => scenario.System.FlushPendingTrophiesAsync(CancellationToken.None).Wait());
            Assert.AreEqual(1, scenario.System.PendingTrophyGrantCount);

            Assert.AreEqual(1, scenario.System.FlushPendingTrophiesAsync(CancellationToken.None).Result);
            Assert.AreEqual(10, scenario.Simulation.LocalTrophies);
        }

        [Test]
        public void LoadPage_Offline_StillLoads_AndReportsUnsentTrophies()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            scenario.System.RecordLevelWin(NormalWin);
            scenario.Simulation.FailNextCall();

            LeaguePageData page = scenario.System.LoadPageAsync(CancellationToken.None).Result;

            Assert.AreEqual(10, page.UnsentTrophies);
            Assert.AreEqual(0, page.Group.LocalTrophies);
        }

        [Test]
        public void LoadPage_RowsCarryZonesRewardsAndLocalFlag()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            scenario.System.RecordLevelWin(NormalWin);
            LeaguePageData page = scenario.System.LoadPageAsync(CancellationToken.None).Result;

            Assert.AreEqual(30, page.Rows.Count);
            Assert.AreEqual("bronze", page.Tier.TierId);
            Assert.AreEqual(LeagueZone.Promotion, page.Rows[0].Zone);
            Assert.AreEqual(LeagueZone.Safe, page.Rows[5].Zone);
            Assert.AreEqual(LeagueZone.Safe, page.Rows[29].Zone, "Bronze không có vùng xuống hạng");
            Assert.AreEqual(LeagueTestFactory.GoldChest, page.Rows[0].Reward.ChestId);
            Assert.AreEqual(LeagueTestFactory.RegularChest, page.Rows[1].Reward.ChestId);
            Assert.IsTrue(page.Rows[page.LocalRowIndex].IsLocal);
            Assert.AreEqual(10, page.Rows[page.LocalRowIndex].Entry.Score);
            Assert.AreEqual(TimeSpan.FromDays(6), page.TimeLeft);
        }

        [Test]
        public void SeasonReward_ClaimedOnce_ThroughGranter()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            scenario.Simulation.DebugSetLocalTrophies(scenario.System.CurrentSeason, 1000000);
            scenario.AdvancePastSeasonEnd();

            SeasonResult result = scenario.System.GetPendingSeasonResultAsync(CancellationToken.None).Result;
            LeagueClaimOutcome first = scenario.System.ClaimSeasonRewardAsync(result.SeasonId, CancellationToken.None).Result;
            LeagueClaimOutcome second = scenario.System.ClaimSeasonRewardAsync(result.SeasonId, CancellationToken.None).Result;

            Assert.AreEqual(LeagueClaimStatus.Granted, first.Status);
            Assert.AreEqual(LeagueClaimStatus.NothingToClaim, second.Status);
            Assert.AreEqual(1, scenario.Granter.GrantedPackages.Count);
            Assert.AreEqual(LeagueTestFactory.GoldChest, scenario.Granter.GrantedPackages[0].ChestId);
        }

        [Test]
        public void SeasonReward_GranterNotReady_StaysPendingAcrossRestart_ThenGrantsOnce()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            scenario.Simulation.DebugSetLocalTrophies(scenario.System.CurrentSeason, 1000000);
            scenario.AdvancePastSeasonEnd();
            scenario.Granter.Ready = false;

            SeasonResult result = scenario.System.GetPendingSeasonResultAsync(CancellationToken.None).Result;
            LeagueClaimOutcome deferred = scenario.System.ClaimSeasonRewardAsync(result.SeasonId, CancellationToken.None).Result;
            Assert.AreEqual(LeagueClaimStatus.Deferred, deferred.Status);
            Assert.AreEqual(1, scenario.System.PendingRewardCount);

            LeagueSystem reopened = scenario.Rebuild();
            Assert.AreEqual(1, reopened.PendingRewardCount, "Quà chờ phát phải sống qua lần mở lại app");

            scenario.Granter.Ready = true;
            Assert.AreEqual(1, reopened.GrantPendingRewards());
            Assert.AreEqual(0, reopened.GrantPendingRewards());
            Assert.AreEqual(LeagueClaimStatus.NothingToClaim, reopened.ClaimSeasonRewardAsync(result.SeasonId, CancellationToken.None).Result.Status);
            Assert.AreEqual(1, scenario.Granter.GrantedPackages.Count);
        }

        [Test]
        public void TrophiesWonBeforeSeasonEnd_FlushedAfter_CountForOldSeason()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            scenario.System.LoadPageAsync(CancellationToken.None).Wait();
            scenario.System.RecordLevelWin(NormalWin);
            string oldSeasonId = scenario.System.CurrentSeason.SeasonId;
            scenario.AdvancePastSeasonEnd();

            SeasonResult result = scenario.System.GetPendingSeasonResultAsync(CancellationToken.None).Result;

            Assert.IsNotNull(result);
            Assert.AreEqual(oldSeasonId, result.SeasonId);
            Assert.AreEqual(10, result.FinalTrophies);
            Assert.AreEqual(0, scenario.System.PendingTrophyGrantCount);
        }

        [Test]
        public void LocalState_SurvivesRestart()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            scenario.System.RecordLevelWin(NormalWin);
            scenario.System.RecordLevelWin(NormalWin);

            LeagueSystem reopened = scenario.Rebuild();

            Assert.AreEqual(2, reopened.Streak.Level);
            Assert.AreEqual(2, reopened.PendingTrophyGrantCount);
        }

        [Test]
        public void StreakStepReward_GrantedOncePerStreakRun()
        {
            var streakReward = new LeagueRewardPackage(string.Empty, new[] { new LeagueRewardItem("booster.magnet", 1) });
            LeagueScenario scenario = LeagueScenario.Create(streakLevelTwoReward: streakReward);
            LeagueSystem system = scenario.System;

            system.RecordLevelWin(NormalWin);
            system.RecordLevelWin(NormalWin);
            system.RecordLevelWin(NormalWin);
            Assert.AreEqual(1, scenario.Granter.GrantedPackages.Count, "Chạm bậc 2 một lần = một phần quà");

            system.RecordStreakEvent(WinStreakEvent.LevelLost);
            system.RecordLevelWin(NormalWin);
            system.RecordLevelWin(NormalWin);
            Assert.AreEqual(2, scenario.Granter.GrantedPackages.Count, "Lượt streak mới chạm lại bậc 2 được quà lần nữa");
            Assert.AreNotEqual(scenario.Granter.GrantedIds[0], scenario.Granter.GrantedIds[1]);
        }

        [Test]
        public void Registry_UnregisterOnlyRemovesSameInstance()
        {
            LeagueSystemRegistry.Reset();
            LeagueSystem first = LeagueScenario.Create().System;
            LeagueSystem second = LeagueScenario.Create().System;
            LeagueSystemRegistry.Register(first);
            LeagueSystemRegistry.Register(second);

            Assert.IsFalse(LeagueSystemRegistry.Unregister(first));
            Assert.IsTrue(LeagueSystemRegistry.TryGet(LeagueScenario.SystemId, out LeagueSystem found));
            Assert.AreSame(second, found);
            LeagueSystemRegistry.Reset();
        }
    }
}
