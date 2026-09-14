using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DreamTech.Leaderboard.League.Tests
{
    [TestFixture]
    public class LeagueSystemTests
    {
        private static readonly LevelWinContext NormalWin = new LevelWinContext(12, 0);

        /// <summary>Độ khó cao nhất của luật cúp trong test (20 cúp gốc × hệ số streak).</summary>
        private static readonly LevelWinContext HardWin = new LevelWinContext(12, 2);

        /// <summary>
        /// Trần số ván thắng khi dồn cúp để đứng nhất nhóm (tối đa 100 cúp / ván ở streak cao nhất, bot mạnh nhất ở tier thấp nhất không
        /// quá 400 cúp cuối mùa). Chạm trần nghĩa là seed / luật mô phỏng đã đổi, không phải lỗi đang kiểm.
        /// </summary>
        private const int MaximumWinsToReachFirstRank = 40;

        /// <summary>Khoảng cách tới mốc đổi mùa của các lượt gọi "ngay trước" / "ngay sau" mốc.</summary>
        private static readonly TimeSpan SeasonBoundaryMargin = TimeSpan.FromMinutes(1);

        /// <summary>Số ván thắng có cúp chờ gửi trong kịch bản lượt hỏi kết quả vắt qua mốc đổi mùa.</summary>
        private const int UnsentWinCount = 3;

        /// <summary>Lượt gọi không bị chặn xong gần như ngay; chờ có hạn để test báo sai thay vì treo.</summary>
        private static readonly TimeSpan UnblockedCallTimeout = TimeSpan.FromSeconds(5);

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

        /// <summary>Playtest C1: hết mùa (chưa gọi dịch vụ) → thắng một ván → cúp đó phải vào mùa mới, không mất.</summary>
        [Test]
        public void FirstWinAfterSeasonEnded_CountsForNewSeason_AndOldSeasonKeepsItsTrophies()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            LeagueSystem system = scenario.System;
            system.LoadPageAsync(CancellationToken.None).Wait();
            int oldSeasonTrophies = system.RecordLevelWin(NormalWin).Trophies;
            system.FlushPendingTrophiesAsync(CancellationToken.None).Wait();
            string oldSeasonId = system.CurrentSeason.SeasonId;

            scenario.AdvancePastSeasonEnd();
            LevelWinOutcome firstWinOfNewSeason = system.RecordLevelWin(NormalWin);
            Assert.AreEqual(1, system.FlushPendingTrophiesAsync(CancellationToken.None).Result);

            LeaguePageData page = system.LoadPageAsync(CancellationToken.None).Result;
            Assert.AreNotEqual(oldSeasonId, page.Season.SeasonId);
            Assert.AreEqual(firstWinOfNewSeason.Trophies, page.Group.LocalTrophies, "Cúp thắng đầu mùa mới bị mất");
            Assert.AreEqual(0, page.UnsentTrophies);

            SeasonResult result = system.GetPendingSeasonResultAsync(CancellationToken.None).Result;
            Assert.AreEqual(oldSeasonId, result.SeasonId);
            Assert.AreEqual(oldSeasonTrophies, result.FinalTrophies);
        }

        [Test]
        public void SeveralWinsQueuedBeforeSeasonEnd_FlushedAfter_AllCountForOldSeason()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            LeagueSystem system = scenario.System;
            system.LoadPageAsync(CancellationToken.None).Wait();
            int queued = 0;
            for (int win = 0; win < 3; win++) queued += system.RecordLevelWin(NormalWin).Trophies;
            string oldSeasonId = system.CurrentSeason.SeasonId;

            scenario.AdvancePastSeasonEnd();
            Assert.AreEqual(3, system.FlushPendingTrophiesAsync(CancellationToken.None).Result);

            SeasonResult result = system.GetPendingSeasonResultAsync(CancellationToken.None).Result;
            Assert.AreEqual(oldSeasonId, result.SeasonId);
            Assert.AreEqual(queued, result.FinalTrophies, "Grant thứ hai trở đi của mùa cũ không được rơi khi mùa bị khép giữa hàng chờ");
            Assert.AreEqual(0, system.LoadPageAsync(CancellationToken.None).Result.Group.LocalTrophies);
        }

        /// <summary>
        /// Đồng hồ lùi (không cắm <see cref="MonotonicLeagueClock"/>): ván thắng gắn vào mùa đã chốt bị dịch vụ từ chối. Grant đó
        /// phải rời hàng chờ (không chặn các ván sau) và được báo ra ngoài, không biến mất im lặng.
        /// </summary>
        [Test]
        public void GrantRejectedByService_LeavesQueue_IsReported_AndDoesNotBlockLaterGrants()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            LeagueSystem system = scenario.System;
            var rejections = new List<LeagueTrophyGrantRejection>();
            system.TrophyGrantRejected += (grant, reason) => rejections.Add(reason);
            system.LoadPageAsync(CancellationToken.None).Wait();
            system.RecordLevelWin(NormalWin);
            SeasonWindow closedSeason = system.CurrentSeason;
            scenario.AdvancePastSeasonEnd();
            SeasonWindow heldSeason = system.CurrentSeason;
            SeasonResult closedResult = system.GetPendingSeasonResultAsync(CancellationToken.None).Result;
            system.AcknowledgeSeasonResultAsync(closedResult.SeasonId, CancellationToken.None).Wait();
            _ = system.ClaimSeasonRewardAsync(closedResult.SeasonId, CancellationToken.None).Result;
            Assume.That(system.PendingTrophyGrantCount, Is.EqualTo(0));

            scenario.Clock.Set(closedSeason.StartUtc + TimeSpan.FromDays(3));
            system.RecordLevelWin(NormalWin);
            Assert.AreEqual(0, system.FlushPendingTrophiesAsync(CancellationToken.None).Result);

            Assert.AreEqual(0, system.PendingTrophyGrantCount);
            CollectionAssert.AreEqual(new[] { LeagueTrophyGrantRejection.SeasonAlreadyFinalized }, rejections);
            Assert.AreEqual(1, system.RejectedTrophyGrantCount);

            scenario.Clock.Set(heldSeason.StartUtc + TimeSpan.FromDays(1));
            LevelWinOutcome later = system.RecordLevelWin(NormalWin);
            Assert.AreEqual(1, system.FlushPendingTrophiesAsync(CancellationToken.None).Result);
            Assert.AreEqual(later.Trophies, system.LoadPageAsync(CancellationToken.None).Result.Group.LocalTrophies);
        }

        /// <summary>
        /// Thắng ở N+1 nhưng gửi thất bại, không gọi được dịch vụ suốt phần còn lại của N+1 (tắt app / mất mạng), lần gửi được đầu
        /// tiên rơi vào N+2. Cúp phải tính cho N+1 và N+1 có kết quả riêng — trước đây grant bị từ chối UnknownSeason và mất.
        /// </summary>
        [Test]
        public void WinInSkippedSeason_SendFailsUntilNextSeason_CountsForSkippedSeason_WithItsOwnResult()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            LeagueSystem system = scenario.System;
            var rejections = new List<LeagueTrophyGrantRejection>();
            system.TrophyGrantRejected += (grant, reason) => rejections.Add(reason);
            int heldSeasonTrophies = system.RecordLevelWin(NormalWin).Trophies;
            Assume.That(system.FlushPendingTrophiesAsync(CancellationToken.None).Result, Is.EqualTo(1));
            string heldSeasonId = system.CurrentSeason.SeasonId;

            scenario.AdvancePastSeasonEnd();
            SeasonWindow skippedSeason = system.CurrentSeason;
            int skippedSeasonTrophies = system.RecordLevelWin(NormalWin).Trophies;
            scenario.Simulation.FailNextCall();
            Assert.Throws<AggregateException>(() => system.FlushPendingTrophiesAsync(CancellationToken.None).Wait());

            // Mở lại app ở mùa sau nữa: grant trong hàng chờ phải còn cửa sổ mùa sau khi đọc lại từ nơi lưu.
            scenario.AdvancePastSeasonEnd();
            system = scenario.Rebuild();
            system.TrophyGrantRejected += (grant, reason) => rejections.Add(reason);
            Assert.AreEqual(1, system.FlushPendingTrophiesAsync(CancellationToken.None).Result);

            CollectionAssert.IsEmpty(rejections);
            Assert.AreEqual(0, system.RejectedTrophyGrantCount);
            LeaguePageData page = system.LoadPageAsync(CancellationToken.None).Result;
            Assert.AreNotEqual(skippedSeason.SeasonId, page.Season.SeasonId);
            Assert.AreEqual(0, page.Group.LocalTrophies, "Cúp của mùa bị nhảy qua không vào mùa hiện tại");

            var shown = new List<SeasonResult>();
            for (SeasonResult pending = system.GetPendingSeasonResultAsync(CancellationToken.None).Result;
                 pending != null && shown.Count < LeagueSeasonFlowDriver.MaximumRounds;
                 pending = system.GetPendingSeasonResultAsync(CancellationToken.None).Result)
            {
                shown.Add(pending);
                system.AcknowledgeSeasonResultAsync(pending.SeasonId, CancellationToken.None).Wait();
                _ = system.ClaimSeasonRewardAsync(pending.SeasonId, CancellationToken.None).Result;
            }
            CollectionAssert.AreEqual(new[] { heldSeasonId, skippedSeason.SeasonId }, shown.Select(result => result.SeasonId));
            Assert.AreEqual(heldSeasonTrophies, shown[0].FinalTrophies);
            Assert.AreEqual(skippedSeasonTrophies, shown[1].FinalTrophies);
        }

        /// <summary>
        /// A1: thắng ở S, chưa gửi lần nào. Sang S+1, lượt gửi đầu hỏng nhưng trang vẫn tải — dịch vụ giữ S+1 (trống) trước khi grant
        /// của S tới. Lần gửi sau grant phải được nhận và S có kết quả đủ cúp, y như khi lượt gửi đầu thành công.
        /// </summary>
        [Test]
        public void WinsOfUnsentSeason_FirstFlushInNextSeasonFails_PageStillLoads_TrophiesStillCountForTheirSeason()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            LeagueSystem system = scenario.System;
            var rejections = new List<LeagueTrophyGrantRejection>();
            system.TrophyGrantRejected += (grant, reason) => rejections.Add(reason);
            int won = system.RecordLevelWin(NormalWin).Trophies + system.RecordLevelWin(NormalWin).Trophies;
            string wonSeasonId = system.CurrentSeason.SeasonId;

            scenario.AdvancePastSeasonEnd();
            scenario.Simulation.FailNextCall();
            LeaguePageData offlinePage = system.LoadPageAsync(CancellationToken.None).Result;
            Assume.That(offlinePage.Season.SeasonId, Is.Not.EqualTo(wonSeasonId));
            Assume.That(scenario.Simulation.ActiveSeasonId, Is.EqualTo(offlinePage.Season.SeasonId), "Dịch vụ phải đã giữ mùa sau trước khi grant tới");
            Assume.That(system.PendingTrophyGrantCount, Is.EqualTo(2));

            LeaguePageData page = system.LoadPageAsync(CancellationToken.None).Result;

            CollectionAssert.IsEmpty(rejections, "Cúp của S không được bị từ chối chỉ vì dịch vụ đã kịp giữ mùa sau");
            Assert.AreEqual(0, system.PendingTrophyGrantCount);
            Assert.AreEqual(0, page.Group.LocalTrophies);
            List<SeasonResult> results = LeagueSeasonFlowDriver.ProcessAllPendingResults(system);
            CollectionAssert.AreEqual(new[] { wonSeasonId }, results.Select(result => result.SeasonId));
            Assert.AreEqual(won, results[0].FinalTrophies);
        }

        /// <summary>
        /// A2 (không cần lỗi mạng): luồng kết quả mùa mở lượt gửi bằng token của nó, trang League (token khác) nhập vào cùng lượt, rồi
        /// token của luồng mùa bị huỷ (đổi màn). Chỉ luồng mùa thôi chờ; lượt gửi chạy tiếp và trang chỉ hỏi bảng mùa mới SAU khi cúp
        /// mùa cũ đã gửi — trước đây trang nhận huỷ không phải của mình, coi như lỗi mạng rồi hỏi bảng mùa mới, grant bị từ chối.
        /// </summary>
        [Test]
        public void SharedFlush_CancelledByOneCaller_KeepsRunningForOtherCaller_AndOldSeasonTrophiesAreNotLost()
        {
            WithoutSynchronizationContext.Run(() =>
            {
                LeagueScenario scenario = LeagueScenario.Create();
                LeagueSystem system = scenario.System;
                var rejections = new List<LeagueTrophyGrantRejection>();
                system.TrophyGrantRejected += (grant, reason) => rejections.Add(reason);
                int won = system.RecordLevelWin(NormalWin).Trophies + system.RecordLevelWin(NormalWin).Trophies;
                string wonSeasonId = system.CurrentSeason.SeasonId;
                scenario.AdvancePastSeasonEnd();
                string newSeasonId = system.CurrentSeason.SeasonId;

                var responseGate = new TaskCompletionSource<bool>();
                scenario.Service.AddTrophiesResponseGate = responseGate;
                var seasonFlowCancellation = new CancellationTokenSource();
                Task<SeasonResult> seasonFlow = system.GetPendingSeasonResultAsync(seasonFlowCancellation.Token);
                Task<LeaguePageData> page = system.LoadPageAsync(CancellationToken.None);
                scenario.Service.AddTrophiesResponseGate = null;
                seasonFlowCancellation.Cancel();

                var cancelled = Assert.Throws<AggregateException>(() => seasonFlow.Wait());
                Assert.IsInstanceOf<OperationCanceledException>(cancelled.InnerException, "Người huỷ thì nhận huỷ");
                Assert.IsFalse(page.IsCompleted, "Người còn chờ không được nhận huỷ của người khác");
                CollectionAssert.DoesNotContain(scenario.Service.CallLog, CountingLeagueGroupService.GetGroupCall + ":" + newSeasonId,
                                                "Chưa gửi xong cúp mùa cũ thì chưa được hỏi bảng mùa mới");

                responseGate.SetResult(true);
                LeaguePageData loaded = page.Result;

                CollectionAssert.AreEqual(new[]
                {
                    CountingLeagueGroupService.AddTrophiesCall + ":" + wonSeasonId,
                    CountingLeagueGroupService.AddTrophiesCall + ":" + wonSeasonId,
                    CountingLeagueGroupService.GetGroupCall + ":" + newSeasonId,
                }, scenario.Service.CallLog);
                CollectionAssert.IsEmpty(rejections);
                Assert.AreEqual(0, system.PendingTrophyGrantCount);
                Assert.AreEqual(newSeasonId, loaded.Season.SeasonId);
                List<SeasonResult> results = LeagueSeasonFlowDriver.ProcessAllPendingResults(system);
                CollectionAssert.AreEqual(new[] { wonSeasonId }, results.Select(result => result.SeasonId));
                Assert.AreEqual(won, results[0].FinalTrophies);
            });
        }

        /// <summary>
        /// Mọi người chờ đều huỷ: không còn ai cần kết quả nên lượt gửi dừng — lượt gọi đang bay bị huỷ (phản hồi mất dù server đã nhận),
        /// grant phía sau không được gửi, cả hai nằm nguyên hàng chờ. Lượt gửi sau mở lượt mới (không nhập vào lượt đã dừng), gửi lại
        /// grant đầu (idempotent, không cộng hai lần) rồi gửi grant sau.
        /// </summary>
        [Test]
        public void SharedFlush_WhenEveryCallerCancels_Stops_AndNextFlushResendsWithoutDoubleCounting()
        {
            WithoutSynchronizationContext.Run(() =>
            {
                LeagueScenario scenario = LeagueScenario.Create();
                LeagueSystem system = scenario.System;
                int firstTrophies = system.RecordLevelWin(NormalWin).Trophies;
                int secondTrophies = system.RecordLevelWin(NormalWin).Trophies;

                var responseGate = new TaskCompletionSource<bool>();
                scenario.Service.AddTrophiesResponseGate = responseGate;
                var firstCaller = new CancellationTokenSource();
                var secondCaller = new CancellationTokenSource();
                Task<int> firstFlush = system.FlushPendingTrophiesAsync(firstCaller.Token);
                Task<int> secondFlush = system.FlushPendingTrophiesAsync(secondCaller.Token);
                scenario.Service.AddTrophiesResponseGate = null;

                // Chờ tới khi Task của từng người huỷ kết thúc: người đó đã rời lượt TRƯỚC khi Task kết thúc.
                firstCaller.Cancel();
                Assert.Throws<AggregateException>(() => firstFlush.Wait());
                Assert.IsTrue(firstFlush.IsCanceled);
                Assert.IsFalse(secondFlush.IsCompleted, "Một người huỷ không dừng lượt gửi của người còn chờ");
                secondCaller.Cancel();
                Assert.Throws<AggregateException>(() => secondFlush.Wait());
                Assert.IsTrue(secondFlush.IsCanceled);

                responseGate.SetResult(true);
                Assert.AreEqual(1, scenario.Service.AddTrophiesCallCount, "Không còn ai chờ: không gửi grant thứ hai");
                Assert.AreEqual(2, system.PendingTrophyGrantCount, "Phản hồi của grant đầu bị huỷ nên grant đó vẫn nằm hàng chờ");
                Assert.AreEqual(firstTrophies, scenario.Simulation.LocalTrophies);

                Assert.AreEqual(2, system.FlushPendingTrophiesAsync(CancellationToken.None).Result);
                Assert.AreEqual(0, system.PendingTrophyGrantCount);
                Assert.AreEqual(firstTrophies + secondTrophies, scenario.Simulation.LocalTrophies, "Gửi lại grant đầu không cộng hai lần");
            });
        }

        /// <summary>
        /// C: đang giữ N có cúp. Thắng ở N+1, gửi hỏng. Ở N+2 rồi N+3 lượt gửi đầu vẫn hỏng nhưng trang vẫn tải — N+2 thành sổ trống đã
        /// khép nằm giữa N+1 và mùa đang giữ. Grant N+1 vẫn phải được tính cho N+1, không bị từ chối.
        /// </summary>
        [Test]
        public void WinInSkippedSeason_WhileLaterSeasonOpensAndClosesEmpty_CountsForSkippedSeason()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            LeagueSystem system = scenario.System;
            var rejections = new List<LeagueTrophyGrantRejection>();
            system.TrophyGrantRejected += (grant, reason) => rejections.Add(reason);
            system.LoadPageAsync(CancellationToken.None).Wait();
            int heldSeasonTrophies = system.RecordLevelWin(NormalWin).Trophies;
            Assume.That(system.FlushPendingTrophiesAsync(CancellationToken.None).Result, Is.EqualTo(1));
            string heldSeasonId = system.CurrentSeason.SeasonId;

            scenario.AdvancePastSeasonEnd();
            string skippedSeasonId = system.CurrentSeason.SeasonId;
            int skippedSeasonTrophies = system.RecordLevelWin(NormalWin).Trophies;
            scenario.Simulation.FailNextCall();
            Assert.Throws<AggregateException>(() => system.FlushPendingTrophiesAsync(CancellationToken.None).Wait());

            scenario.AdvancePastSeasonEnd();
            string emptySeasonId = system.CurrentSeason.SeasonId;
            scenario.Simulation.FailNextCall();
            system.LoadPageAsync(CancellationToken.None).Wait();
            scenario.AdvancePastSeasonEnd();
            scenario.Simulation.FailNextCall();
            system.LoadPageAsync(CancellationToken.None).Wait();
            Assume.That(scenario.Simulation.ActiveSeasonId, Is.EqualTo(system.CurrentSeason.SeasonId));
            Assume.That(system.PendingTrophyGrantCount, Is.EqualTo(1));

            LeaguePageData page = system.LoadPageAsync(CancellationToken.None).Result;

            CollectionAssert.IsEmpty(rejections);
            Assert.AreEqual(0, system.PendingTrophyGrantCount);
            Assert.AreEqual(0, page.Group.LocalTrophies);
            List<SeasonResult> results = LeagueSeasonFlowDriver.ProcessAllPendingResults(system);
            CollectionAssert.AreEqual(new[] { heldSeasonId, skippedSeasonId }, results.Select(result => result.SeasonId));
            CollectionAssert.DoesNotContain(results.Select(result => result.SeasonId), emptySeasonId);
            Assert.AreEqual(heldSeasonTrophies, results[0].FinalTrophies);
            Assert.AreEqual(skippedSeasonTrophies, results[1].FinalTrophies);
        }

        /// <summary>
        /// Mùa đang giữ có cúp đã gửi (đứng nhất, lên hạng, có rương), rồi gửi hỏng suốt <see cref="SimulatedLeagueOptions.MaximumStoredResults"/>
        /// mùa liền (mỗi mùa thắng một ván), lần gửi bù được đầu tiên rơi vào mùa sau nữa. Mỗi mùa bị nhảy qua dựng một sổ có kết quả
        /// chờ nên số sổ còn chờ vượt giới hạn. Trước đây phần thu gọn sổ bỏ luôn sổ cũ nhất: mùa lên hạng biến mất khỏi luồng kết quả
        /// và rương của nó không bao giờ nhận được.
        /// </summary>
        [Test]
        public void CatchUpFlushAfterFailedSeasonsBeyondStoredLimit_KeepsEveryPendingResult_IncludingPromotedSeasonBeforeThem()
        {
            SimulatedLeagueOptions options = LeagueTestFactory.CreateSimulationOptions();
            int failedSeasonCount = options.MaximumStoredResults;
            LeagueScenario scenario = LeagueScenario.Create(options);
            LeagueSystem system = scenario.System;
            var rejections = new List<LeagueTrophyGrantRejection>();
            system.TrophyGrantRejected += (grant, reason) => rejections.Add(reason);

            // Mùa đầu: dồn đủ cúp để giữ hạng nhất tới hết mùa, gửi được.
            SeasonWindow promotedSeason = system.CurrentSeason;
            long trophiesForFirstRank = scenario.Simulation.DebugTrophiesToReachRank(promotedSeason, 0, projectToSeasonEnd: true);
            int promotedSeasonTrophies = 0;
            for (int win = 0; win < MaximumWinsToReachFirstRank && promotedSeasonTrophies < trophiesForFirstRank; win++)
            {
                promotedSeasonTrophies += system.RecordLevelWin(HardWin).Trophies;
            }
            Assume.That(promotedSeasonTrophies, Is.GreaterThanOrEqualTo(trophiesForFirstRank));
            Assume.That(system.FlushPendingTrophiesAsync(CancellationToken.None).Result, Is.GreaterThan(0));

            // Các mùa sau: thắng một ván mỗi mùa, lần gửi nào cũng hỏng.
            var skippedSeasonIds = new List<string>();
            for (int season = 0; season < failedSeasonCount; season++)
            {
                scenario.AdvancePastSeasonEnd();
                skippedSeasonIds.Add(system.CurrentSeason.SeasonId);
                system.RecordLevelWin(NormalWin);
                scenario.Simulation.FailNextCall();
                Assert.Throws<AggregateException>(() => system.FlushPendingTrophiesAsync(CancellationToken.None).Wait());
            }
            Assume.That(scenario.Simulation.ActiveSeasonId, Is.EqualTo(promotedSeason.SeasonId), "Dịch vụ phải còn giữ mùa đầu");

            // Mùa sau nữa: thắng rồi gửi bù được cả hàng chờ.
            scenario.AdvancePastSeasonEnd();
            system.RecordLevelWin(NormalWin);
            Assert.AreEqual(failedSeasonCount + 1, system.FlushPendingTrophiesAsync(CancellationToken.None).Result);
            CollectionAssert.IsEmpty(rejections);

            // Luồng kết quả như game: xem xong rồi nhận rương, từng kết quả.
            var shown = new List<SeasonResult>();
            var claimStatuses = new List<LeagueClaimStatus>();
            for (SeasonResult pending = system.GetPendingSeasonResultAsync(CancellationToken.None).Result;
                 pending != null && shown.Count < LeagueSeasonFlowDriver.MaximumRounds;
                 pending = system.GetPendingSeasonResultAsync(CancellationToken.None).Result)
            {
                shown.Add(pending);
                system.AcknowledgeSeasonResultAsync(pending.SeasonId, CancellationToken.None).Wait();
                claimStatuses.Add(system.ClaimSeasonRewardAsync(pending.SeasonId, CancellationToken.None).Result.Status);
            }

            var expectedSeasonIds = new List<string> { promotedSeason.SeasonId };
            expectedSeasonIds.AddRange(skippedSeasonIds);
            CollectionAssert.AreEqual(expectedSeasonIds, shown.Select(result => result.SeasonId),
                                      "Sổ còn kết quả chờ không được bị bỏ khi thu gọn — kể cả khi số sổ vượt giới hạn");
            SeasonResult promoted = shown[0];
            Assert.AreEqual(SeasonOutcome.Promoted, promoted.Outcome);
            Assert.AreEqual(promotedSeasonTrophies, promoted.FinalTrophies);
            Assert.IsFalse(promoted.Reward.IsEmpty, "Mùa lên hạng phải có rương");
            Assert.AreEqual(LeagueClaimStatus.Granted, claimStatuses[0], "Rương của mùa lên hạng phải nhận được");
            Assert.AreEqual(promoted.TierIndexAfter, shown[1].TierIndexBefore, "Mùa bị nhảy qua đầu tiên bắt đầu ở bậc sau khi lên hạng");
            Assert.IsNull(system.GetPendingSeasonResultAsync(CancellationToken.None).Result);
        }

        [Test]
        public void LocalState_PendingGrantKeepsSeasonWindow_AndEntryWithoutWindowStillLoads()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            scenario.System.RecordLevelWin(NormalWin);
            SeasonWindow season = scenario.System.CurrentSeason;
            Assert.IsTrue(scenario.Store.TryRead("league." + LeagueScenario.SystemId + ".local", out string stored));

            LeagueLocalState decoded = LeagueLocalState.Decode(stored);
            Assert.AreEqual(1, decoded.PendingTrophyGrants.Count);
            Assert.AreEqual(season, decoded.PendingTrophyGrants[0].Season);

            // Bản lưu trước khi grant có cửa sổ mùa: vẫn đọc được, grant chỉ có id mùa.
            var withoutWindow = new LeagueLocalState();
            withoutWindow.PendingTrophyGrants.Add(new LeagueTrophyGrant("trophy-old", season.SeasonId, 10));
            LeagueLocalState reloaded = LeagueLocalState.Decode(withoutWindow.Encode());
            Assert.AreEqual(1, reloaded.PendingTrophyGrants.Count);
            Assert.AreEqual(season.SeasonId, reloaded.PendingTrophyGrants[0].SeasonId);
            Assert.IsNull(reloaded.PendingTrophyGrants[0].Season);
        }

        /// <summary>
        /// Máy dev/QA còn save định dạng 1 của 0.2.0 (mùa đang giữ ở tương lai, kết quả trùng mùa): nâng lên thì dịch vụ bắt đầu lại,
        /// thắng trong mùa theo đồng hồ được tính đủ, không grant nào bị từ chối.
        /// </summary>
        [Test]
        public void Format1SimulationSaveFrom020_WinsAfterUpgrade_AllCount()
        {
            var clock = LeagueTestFactory.CreateClockInFirstSeason();
            FixedLengthSeasonSchedule schedule = LeagueTestFactory.CreateSchedule();
            SeasonWindow season = schedule.GetSeasonAt(clock.UtcNow);
            SeasonWindow futureSeason = schedule.GetSeasonAt(season.EndUtc);
            var format1 = new LeagueTextRecord(1);
            format1.SetInt("tier", 0);
            format1.SetString("active.season", futureSeason.SeasonId);
            format1.SetLong("active.start", futureSeason.StartUtc.Ticks);
            format1.SetLong("active.end", futureSeason.EndUtc.Ticks);
            format1.SetInt("results", 1);
            format1.SetResult("result0", new SeasonResult(season.SeasonId, 0, 0, SeasonOutcome.Unchanged, 13, 30, 150, LeagueRewardPackage.None,
                                                          acknowledged: true, rewardClaimed: true));
            var store = new InMemoryLeagueTextStore();
            store.Write(LeagueTestFactory.CreateSimulationOptions().StoreKey, format1.Encode());

            LeagueScenario scenario = LeagueScenario.Create(store: store, clock: clock);
            int won = 0;
            for (int win = 0; win < 3; win++) won += scenario.System.RecordLevelWin(NormalWin).Trophies;
            Assert.AreEqual(3, scenario.System.FlushPendingTrophiesAsync(CancellationToken.None).Result);

            Assert.AreEqual(0, scenario.System.RejectedTrophyGrantCount);
            LeaguePageData page = scenario.System.LoadPageAsync(CancellationToken.None).Result;
            Assert.AreEqual(season.SeasonId, page.Season.SeasonId);
            Assert.AreEqual(won, page.Group.LocalTrophies);
        }

        [Test]
        public void PendingSeasonResult_WithheldWhileOldSeasonTrophiesCannotBeSent()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            LeagueSystem system = scenario.System;
            system.LoadPageAsync(CancellationToken.None).Wait();
            int trophies = system.RecordLevelWin(NormalWin).Trophies;
            scenario.AdvancePastSeasonEnd();
            int pendingCallsBefore = scenario.Service.CallLog.Count(call => call.StartsWith(CountingLeagueGroupService.GetPendingCall));

            scenario.Simulation.FailNextCall();
            Assert.IsNull(system.GetPendingSeasonResultAsync(CancellationToken.None).Result,
                          "Còn cúp mùa cũ chưa gửi được thì chưa hiện kết quả (hiện rồi bấm xem là cúp tới sau bị từ chối)");
            Assert.AreEqual(1, system.PendingTrophyGrantCount);
            Assert.AreEqual(pendingCallsBefore, scenario.Service.CallLog.Count(call => call.StartsWith(CountingLeagueGroupService.GetPendingCall)));

            SeasonResult result = system.GetPendingSeasonResultAsync(CancellationToken.None).Result;
            Assert.IsNotNull(result);
            Assert.AreEqual(trophies, result.FinalTrophies);
        }

        /// <summary>
        /// Thứ tự yêu cầu đảo qua mốc đổi mùa. 23:59: luồng kết quả mùa gửi hỏng, cúp chờ gửi còn là của mùa hiện tại nên lần kiểm đầu
        /// cho qua, yêu cầu hỏi kết quả tới dịch vụ muộn. 00:01: trang League mở, gửi vẫn hỏng, lượt hỏi bảng mùa mới khép mùa cũ thiếu
        /// cúp. Rồi yêu cầu của luồng mới được xử lý và nhận kết quả thiếu cúp đó. Trước đây kết quả được trả về: người chơi xem xong là
        /// chốt, mạng về thì mọi cúp chờ gửi bị từ chối <see cref="LeagueTrophyGrantRejection.SeasonAlreadyFinalized"/>. Giờ trả null
        /// (lượt sau thử lại) và cúp được tính vào kết quả.
        /// </summary>
        [Test]
        public void PendingResultRequestProcessedAfterSeasonRoll_ResultOfSeasonWithUnsentTrophiesIsWithheld_TrophiesAreNotRejected()
        {
            WithoutSynchronizationContext.Run(() =>
            {
                LeagueScenario scenario = LeagueScenario.Create();
                LeagueSystem system = scenario.System;
                var rejections = new List<LeagueTrophyGrantRejection>();
                system.TrophyGrantRejected += (grant, reason) => rejections.Add(reason);
                system.LoadPageAsync(CancellationToken.None).Wait();
                int sentTrophies = system.RecordLevelWin(NormalWin).Trophies;
                Assume.That(system.FlushPendingTrophiesAsync(CancellationToken.None).Result, Is.EqualTo(1));
                SeasonWindow closingSeason = system.CurrentSeason;
                int unsentTrophies = 0;
                for (int win = 0; win < UnsentWinCount; win++) unsentTrophies += system.RecordLevelWin(NormalWin).Trophies;
                scenario.Simulation.FailNextCall();
                Assert.Throws<AggregateException>(() => system.FlushPendingTrophiesAsync(CancellationToken.None).Wait());

                // 23:59 — luồng kết quả mùa: gửi hỏng, yêu cầu hỏi kết quả đang bay.
                scenario.Clock.Set(closingSeason.EndUtc - SeasonBoundaryMargin);
                var pendingRequest = new TaskCompletionSource<bool>();
                scenario.Service.GetPendingRequestGate = pendingRequest;
                scenario.Simulation.FailNextCall();
                Task<SeasonResult> seasonFlow = system.GetPendingSeasonResultAsync(CancellationToken.None);
                scenario.Service.GetPendingRequestGate = null;
                Assume.That(seasonFlow.IsCompleted, Is.False, "Yêu cầu hỏi kết quả phải còn đang bay");

                // 00:01 — trang League: gửi hỏng, hỏi bảng mùa mới làm dịch vụ khép mùa cũ khi chưa có các grant chờ gửi.
                scenario.Clock.Set(closingSeason.EndUtc + SeasonBoundaryMargin);
                scenario.Simulation.FailNextCall();
                LeaguePageData page = system.LoadPageAsync(CancellationToken.None).Result;
                Assume.That(page.Season.SeasonId, Is.Not.EqualTo(closingSeason.SeasonId));
                Assume.That(scenario.Simulation.ActiveSeasonId, Is.EqualTo(page.Season.SeasonId), "Dịch vụ phải đã khép mùa cũ");

                pendingRequest.SetResult(true);
                Assert.IsTrue(seasonFlow.Wait(UnblockedCallTimeout));
                Assert.IsNull(seasonFlow.Result, "Kết quả của mùa còn cúp chờ gửi không được hiện: xem xong là chốt, cúp tới sau bị từ chối");
                Assert.AreEqual(UnsentWinCount, system.PendingTrophyGrantCount);

                // Mạng về.
                Assert.AreEqual(UnsentWinCount, system.FlushPendingTrophiesAsync(CancellationToken.None).Result);
                CollectionAssert.IsEmpty(rejections, "Không grant nào của mùa cũ được bị từ chối");
                Assert.AreEqual(0, system.RejectedTrophyGrantCount);
                List<SeasonResult> results = LeagueSeasonFlowDriver.ProcessAllPendingResults(system);
                CollectionAssert.AreEqual(new[] { closingSeason.SeasonId }, results.Select(result => result.SeasonId));
                Assert.AreEqual(sentTrophies + unsentTrophies, results[0].FinalTrophies);
            });
        }

        /// <summary>
        /// Backend nhiều kết nối: yêu cầu hỏi kết quả của luồng (gửi lúc 23:59) được xử lý SAU khi trang League (00:01) đã khép mùa thiếu
        /// cúp, còn phản hồi về máy SAU khi mạng có lại và các cúp muộn đã gửi xong. Lúc phản hồi tới, hàng chờ trống nên kiểm theo hàng
        /// chờ không bắt được; bản kết quả trong tay là bản tính trước khi cúp tới. Kết quả thuộc đúng mùa đọc trước lượt gọi phải bị bỏ,
        /// lượt sau nhận bản đủ cúp.
        /// </summary>
        [Test]
        public void PendingResultResponseArrivesAfterLateTrophiesWereSent_StaleResultOfClosingSeasonIsWithheld()
        {
            WithoutSynchronizationContext.Run(() =>
            {
                LeagueScenario scenario = LeagueScenario.Create();
                LeagueSystem system = scenario.System;
                var rejections = new List<LeagueTrophyGrantRejection>();
                system.TrophyGrantRejected += (grant, reason) => rejections.Add(reason);
                system.LoadPageAsync(CancellationToken.None).Wait();
                int sentTrophies = system.RecordLevelWin(NormalWin).Trophies;
                Assume.That(system.FlushPendingTrophiesAsync(CancellationToken.None).Result, Is.EqualTo(1));
                SeasonWindow closingSeason = system.CurrentSeason;
                int unsentTrophies = 0;
                for (int win = 0; win < UnsentWinCount; win++) unsentTrophies += system.RecordLevelWin(NormalWin).Trophies;
                scenario.Simulation.FailNextCall();
                Assert.Throws<AggregateException>(() => system.FlushPendingTrophiesAsync(CancellationToken.None).Wait());

                // 23:59 — luồng kết quả mùa: gửi hỏng, yêu cầu hỏi kết quả tới dịch vụ muộn VÀ phản hồi về muộn.
                scenario.Clock.Set(closingSeason.EndUtc - SeasonBoundaryMargin);
                var pendingRequest = new TaskCompletionSource<bool>();
                var pendingResponse = new TaskCompletionSource<bool>();
                scenario.Service.GetPendingRequestGate = pendingRequest;
                scenario.Service.GetPendingResponseGate = pendingResponse;
                scenario.Simulation.FailNextCall();
                Task<SeasonResult> seasonFlow = system.GetPendingSeasonResultAsync(CancellationToken.None);
                scenario.Service.GetPendingRequestGate = null;
                scenario.Service.GetPendingResponseGate = null;
                Assume.That(seasonFlow.IsCompleted, Is.False, "Yêu cầu hỏi kết quả phải còn đang bay");

                // 00:01 — trang League: gửi hỏng, hỏi bảng mùa mới làm dịch vụ khép mùa cũ khi chưa có các grant chờ gửi.
                scenario.Clock.Set(closingSeason.EndUtc + SeasonBoundaryMargin);
                scenario.Simulation.FailNextCall();
                LeaguePageData page = system.LoadPageAsync(CancellationToken.None).Result;
                Assume.That(scenario.Simulation.ActiveSeasonId, Is.EqualTo(page.Season.SeasonId), "Dịch vụ phải đã khép mùa cũ");

                // Dịch vụ xử lý yêu cầu của luồng lúc này: tính ra kết quả THIẾU cúp, phản hồi chưa về máy.
                pendingRequest.SetResult(true);

                // Mạng về, một lượt gửi khác đẩy hết cúp muộn: dịch vụ tính lại kết quả đủ cúp, hàng chờ trên máy trống.
                Assert.AreEqual(UnsentWinCount, system.FlushPendingTrophiesAsync(CancellationToken.None).Result);
                Assume.That(system.PendingTrophyGrantCount, Is.EqualTo(0));

                pendingResponse.SetResult(true);
                Assert.IsTrue(seasonFlow.Wait(UnblockedCallTimeout));
                Assert.IsNull(seasonFlow.Result, "Phản hồi mang bản kết quả tính trước khi cúp muộn tới không được hiện");

                CollectionAssert.IsEmpty(rejections);
                List<SeasonResult> results = LeagueSeasonFlowDriver.ProcessAllPendingResults(system);
                CollectionAssert.AreEqual(new[] { closingSeason.SeasonId }, results.Select(result => result.SeasonId));
                Assert.AreEqual(sentTrophies + unsentTrophies, results[0].FinalTrophies);
            });
        }

        [Test]
        public void LoadPage_AfterSeasonEnd_WhileOffline_DoesNotShowOldSeasonTrophiesAsUnsent_AndTheyStillCountLater()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            LeagueSystem system = scenario.System;
            system.LoadPageAsync(CancellationToken.None).Wait();
            int trophies = system.RecordLevelWin(NormalWin).Trophies;
            string oldSeasonId = system.CurrentSeason.SeasonId;
            scenario.AdvancePastSeasonEnd();

            // Gửi hỏng nhưng tải bảng được: dịch vụ khép mùa cũ khi chưa có grant.
            scenario.Simulation.FailNextCall();
            LeaguePageData page = system.LoadPageAsync(CancellationToken.None).Result;
            Assert.AreNotEqual(oldSeasonId, page.Season.SeasonId);
            Assert.AreEqual(0, page.UnsentTrophies, "Cúp nợ của mùa cũ không phải điểm của mùa mới");
            Assert.AreEqual(trophies, system.UnsentTrophies);

            // Lần sau gửi được: grant tới muộn cập nhật kết quả mùa cũ (chưa ai xem).
            SeasonResult result = system.GetPendingSeasonResultAsync(CancellationToken.None).Result;
            Assert.AreEqual(oldSeasonId, result.SeasonId);
            Assert.AreEqual(trophies, result.FinalTrophies);
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
