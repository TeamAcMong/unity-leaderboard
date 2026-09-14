using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DreamTech.Leaderboard.League.Tests
{
    /// <summary>
    /// Hợp đồng mà MỌI bản cài <see cref="ILeagueGroupService"/> phải giữ. Viết adapter mới (backend thật...) thì tạo một lớp con
    /// cài <see cref="CreateService"/> — cùng bộ test này chạy lại, pass thì cắm vào game được.
    /// </summary>
    public abstract class LeagueGroupServiceContract
    {
        /// <summary>Vào mùa sau mốc bắt đầu chừng này — đủ xa mốc để không dính làm tròn, đủ gần để bot chưa kiếm nhiều.</summary>
        private static readonly TimeSpan IntoSeason = TimeSpan.FromHours(1);

        protected ManualLeagueClock Clock { get; private set; }
        protected FixedLengthSeasonSchedule Schedule { get; private set; }
        protected LeagueRules Rules { get; private set; }

        /// <summary>Tạo dịch vụ mới tinh (chưa có dữ liệu) dùng <see cref="Clock"/> và <see cref="Rules"/>.</summary>
        protected abstract ILeagueGroupService CreateService();

        /// <summary>Đặt người chơi vào hạng nhất nhóm (để test phần thưởng hạng nhất).</summary>
        protected abstract void MakeLocalPlayerFirst(ILeagueGroupService service, SeasonWindow season);

        private SeasonWindow CurrentSeason => Schedule.GetSeasonAt(Clock.UtcNow);

        private SeasonWindow SeasonAfter(SeasonWindow season)
        {
            return Schedule.GetSeasonAt(season.EndUtc);
        }

        private void MoveInto(SeasonWindow season)
        {
            Clock.Set(season.StartUtc + IntoSeason);
        }

        private List<SeasonResult> ProcessAllPendingResults(ILeagueGroupService service)
        {
            return LeagueSeasonFlowDriver.ProcessAllPendingResults(service, () => CurrentSeason);
        }

        /// <summary>Grant mang cửa sổ mùa, đúng như <see cref="LeagueSystem.RecordLevelWin"/> tạo.</summary>
        private static LeagueTrophyGrant Grant(string grantId, SeasonWindow season, int trophies)
        {
            return new LeagueTrophyGrant(grantId, season, trophies);
        }

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

        // ---------------------------------------------------------------- 0.2.1: không mất cúp, không lùi mùa, không kết quả trùng

        [Test]
        public void Contract_FirstWinOfNewSeason_WhileOldSeasonStillHeld_IsCounted()
        {
            ILeagueGroupService service = CreateService();
            SeasonWindow oldSeason = CurrentSeason;
            service.AddTrophiesAsync(oldSeason, Grant("grant-old", oldSeason, 40), CancellationToken.None).Wait();

            // Hết mùa trong lúc không gọi dịch vụ; ván thắng đầu tiên của mùa mới (x5 streak) là lượt gọi đầu tiên.
            SeasonWindow newSeason = SeasonAfter(oldSeason);
            MoveInto(newSeason);
            LeagueGroupSnapshot group = service.AddTrophiesAsync(CurrentSeason, Grant("grant-new", newSeason, 100), CancellationToken.None).Result;

            Assert.AreEqual(newSeason.SeasonId, group.Season.SeasonId);
            Assert.AreEqual(100, group.LocalTrophies, "Cúp thắng đầu mùa mới không được mất");
            SeasonResult oldResult = service.GetPendingResultAsync(CurrentSeason, CancellationToken.None).Result;
            Assert.IsNotNull(oldResult);
            Assert.AreEqual(oldSeason.SeasonId, oldResult.SeasonId);
            Assert.AreEqual(40, oldResult.FinalTrophies);
        }

        [Test]
        public void Contract_QueuedOldSeasonGrants_AfterRollover_AllCountForOldSeason()
        {
            ILeagueGroupService service = CreateService();
            SeasonWindow oldSeason = CurrentSeason;
            service.AddTrophiesAsync(oldSeason, Grant("grant-a", oldSeason, 5), CancellationToken.None).Wait();

            // Ba ván thắng cuối mùa cũ nằm trong hàng chờ, chỉ được gửi sau khi mùa mới đã bắt đầu.
            MoveInto(SeasonAfter(oldSeason));
            service.AddTrophiesAsync(CurrentSeason, Grant("grant-b", oldSeason, 7), CancellationToken.None).Wait();
            service.AddTrophiesAsync(CurrentSeason, Grant("grant-c", oldSeason, 9), CancellationToken.None).Wait();
            service.AddTrophiesAsync(CurrentSeason, Grant("grant-d", oldSeason, 11), CancellationToken.None).Wait();
            service.AddTrophiesAsync(CurrentSeason, Grant("grant-c", oldSeason, 9), CancellationToken.None).Wait();

            SeasonResult oldResult = service.GetPendingResultAsync(CurrentSeason, CancellationToken.None).Result;
            Assert.IsNotNull(oldResult);
            Assert.AreEqual(oldSeason.SeasonId, oldResult.SeasonId);
            Assert.AreEqual(32, oldResult.FinalTrophies, "Mọi grant của mùa cũ đều tính, grant gửi lại chỉ tính một lần");
            Assert.AreEqual(0, service.GetGroupAsync(CurrentSeason, CancellationToken.None).Result.LocalTrophies);
        }

        [Test]
        public void Contract_LateGrant_ForClosedSeasonNotYetAcknowledged_RecalculatesResultAndNextTier()
        {
            ILeagueGroupService service = CreateService();
            SeasonWindow oldSeason = CurrentSeason;
            service.AddTrophiesAsync(oldSeason, Grant("grant-a", oldSeason, 5), CancellationToken.None).Wait();
            MoveInto(SeasonAfter(oldSeason));
            SeasonResult before = service.GetPendingResultAsync(CurrentSeason, CancellationToken.None).Result;
            Assume.That(before.FinalRank, Is.GreaterThan(0), "5 cúp phải chưa đủ hạng nhất");

            // Grant tới muộn đủ lớn để đổi hạng cuối → đổi outcome, rương và bậc mùa sau (mùa sau chưa có cúp).
            service.AddTrophiesAsync(CurrentSeason, Grant("grant-late", oldSeason, 1000000), CancellationToken.None).Wait();

            SeasonResult after = service.GetPendingResultAsync(CurrentSeason, CancellationToken.None).Result;
            Assert.AreEqual(oldSeason.SeasonId, after.SeasonId);
            Assert.AreEqual(1000005, after.FinalTrophies);
            Assert.AreEqual(0, after.FinalRank);
            Assert.AreEqual(SeasonOutcome.Promoted, after.Outcome);
            Assert.AreEqual(LeagueTestFactory.GoldChest, after.Reward.ChestId);
            Assert.AreEqual(1, service.GetGroupAsync(CurrentSeason, CancellationToken.None).Result.TierIndex, "Mùa sau chưa có cúp thì lên bậc theo kết quả mới");
            Assert.AreEqual(1, ProcessAllPendingResults(service).Count, "Tính lại không được sinh kết quả thứ hai");
        }

        [Test]
        public void Contract_LateGrant_AfterResultAcknowledged_IsRejected_AndResultUnchanged()
        {
            ILeagueGroupService service = CreateService();
            SeasonWindow oldSeason = CurrentSeason;
            service.AddTrophiesAsync(oldSeason, Grant("grant-a", oldSeason, 5), CancellationToken.None).Wait();
            MoveInto(SeasonAfter(oldSeason));
            SeasonResult shown = service.GetPendingResultAsync(CurrentSeason, CancellationToken.None).Result;
            service.AcknowledgeResultAsync(shown.SeasonId, CancellationToken.None).Wait();

            var failure = Assert.Throws<AggregateException>(() =>
                service.AddTrophiesAsync(CurrentSeason, Grant("grant-late", oldSeason, 50), CancellationToken.None).Wait());
            var rejection = failure.InnerException as LeagueTrophyGrantRejectedException;
            Assert.IsNotNull(rejection, "Từ chối phải là LeagueTrophyGrantRejectedException, không im lặng");
            Assert.AreEqual(LeagueTrophyGrantRejection.SeasonAlreadyFinalized, rejection.Reason);
            Assert.AreEqual("grant-late", rejection.Grant.GrantId);

            Assert.DoesNotThrow(() => service.AddTrophiesAsync(CurrentSeason, Grant("grant-a", oldSeason, 5), CancellationToken.None).Wait(),
                                "Gửi lại grant đã tính là idempotent, không phải bị từ chối");

            SeasonResult pending = service.GetPendingResultAsync(CurrentSeason, CancellationToken.None).Result;
            Assert.IsNotNull(pending, "Rương chưa nhận nên kết quả vẫn còn việc");
            Assert.AreEqual(oldSeason.SeasonId, pending.SeasonId);
            Assert.AreEqual(5, pending.FinalTrophies);
            Assert.IsTrue(pending.Acknowledged);
        }

        [Test]
        public void Contract_EachSeasonHasAtMostOneResult_AcrossRepeatedCallsAndRollovers()
        {
            ILeagueGroupService service = CreateService();
            SeasonWindow firstSeason = CurrentSeason;
            service.AddTrophiesAsync(firstSeason, Grant("grant-a", firstSeason, 10), CancellationToken.None).Wait();

            SeasonWindow secondSeason = SeasonAfter(firstSeason);
            MoveInto(secondSeason);
            for (int call = 0; call < 3; call++)
            {
                service.GetGroupAsync(CurrentSeason, CancellationToken.None).Wait();
                _ = service.GetPendingResultAsync(CurrentSeason, CancellationToken.None).Result;
            }
            List<SeasonResult> firstRound = ProcessAllPendingResults(service);
            service.AddTrophiesAsync(CurrentSeason, Grant("grant-b", secondSeason, 20), CancellationToken.None).Wait();

            MoveInto(SeasonAfter(secondSeason));
            service.GetGroupAsync(CurrentSeason, CancellationToken.None).Wait();
            List<SeasonResult> secondRound = ProcessAllPendingResults(service);

            CollectionAssert.AreEqual(new[] { firstSeason.SeasonId }, firstRound.Select(result => result.SeasonId));
            CollectionAssert.AreEqual(new[] { secondSeason.SeasonId }, secondRound.Select(result => result.SeasonId));
            Assert.AreEqual(20, secondRound[0].FinalTrophies);
            Assert.IsNull(service.GetPendingResultAsync(CurrentSeason, CancellationToken.None).Result);
        }

        [Test]
        public void Contract_AcknowledgeAndClaim_AreIdempotent_InAnyOrder_AndClearPending()
        {
            ILeagueGroupService service = CreateService();
            SeasonWindow firstSeason = CurrentSeason;
            MakeLocalPlayerFirst(service, firstSeason);
            MoveInto(SeasonAfter(firstSeason));
            SeasonResult result = service.GetPendingResultAsync(CurrentSeason, CancellationToken.None).Result;

            // Nhận rương trước, xem sau (thứ tự demo dùng), mỗi bước gọi hai lần.
            LeagueRewardPackage first = service.ClaimSeasonRewardAsync(result.SeasonId, CancellationToken.None).Result;
            LeagueRewardPackage second = service.ClaimSeasonRewardAsync(result.SeasonId, CancellationToken.None).Result;
            service.AcknowledgeResultAsync(result.SeasonId, CancellationToken.None).Wait();
            service.AcknowledgeResultAsync(result.SeasonId, CancellationToken.None).Wait();

            Assert.AreEqual(LeagueTestFactory.GoldChest, first.ChestId);
            Assert.IsTrue(second.IsEmpty);
            Assert.IsNull(service.GetPendingResultAsync(CurrentSeason, CancellationToken.None).Result);
            Assert.DoesNotThrow(() => service.AcknowledgeResultAsync("season-unknown", CancellationToken.None).Wait());
            Assert.IsTrue(service.ClaimSeasonRewardAsync("season-unknown", CancellationToken.None).Result.IsEmpty);
        }

        [Test]
        public void Contract_ClockGoingBack_NeverReopensClosedSeason_NorResetsHeldSeason()
        {
            ILeagueGroupService service = CreateService();
            SeasonWindow closedSeason = CurrentSeason;
            service.AddTrophiesAsync(closedSeason, Grant("grant-a", closedSeason, 10), CancellationToken.None).Wait();
            SeasonWindow heldSeason = SeasonAfter(closedSeason);
            MoveInto(heldSeason);
            service.AddTrophiesAsync(CurrentSeason, Grant("grant-b", heldSeason, 30), CancellationToken.None).Wait();
            Assert.AreEqual(1, ProcessAllPendingResults(service).Count);

            // Đồng hồ lùi về giữa mùa đã khép (mất offset cheat khi mở lại app, hoặc chỉnh giờ máy).
            Clock.Set(closedSeason.StartUtc + TimeSpan.FromDays(2));
            LeagueGroupSnapshot group = service.GetGroupAsync(CurrentSeason, CancellationToken.None).Result;

            Assert.AreEqual(heldSeason.SeasonId, group.Season.SeasonId, "Không mở lại mùa đã khép");
            Assert.AreEqual(30, group.LocalTrophies, "Không xoá cúp của mùa đang giữ");
            Assert.IsNull(service.GetPendingResultAsync(CurrentSeason, CancellationToken.None).Result);

            // Giờ chạy tiếp tới sau mùa đang giữ: chỉ đúng một kết quả mới, của mùa đang giữ.
            MoveInto(SeasonAfter(heldSeason));
            List<SeasonResult> results = ProcessAllPendingResults(service);
            CollectionAssert.AreEqual(new[] { heldSeason.SeasonId }, results.Select(result => result.SeasonId));
            Assert.AreEqual(30, results[0].FinalTrophies);
        }

        /// <summary>
        /// Trạng thái (a): dịch vụ còn giữ N. Thắng ở N+1 nhưng mọi lần gửi trong N+1 thất bại; lần gửi được đầu tiên rơi vào N+2.
        /// Grant N+1 phải được tính cho N+1 (có kết quả riêng), N khép trước, không mất cúp.
        /// </summary>
        [Test]
        public void Contract_GrantOfSkippedSeason_WhileOlderSeasonStillHeld_CountsForSkippedSeason()
        {
            ILeagueGroupService service = CreateService();
            SeasonWindow heldSeason = CurrentSeason;
            service.AddTrophiesAsync(heldSeason, Grant("grant-held", heldSeason, 5), CancellationToken.None).Wait();
            SeasonWindow skippedSeason = SeasonAfter(heldSeason);
            SeasonWindow laterSeason = SeasonAfter(skippedSeason);

            MoveInto(laterSeason);
            LeagueGroupSnapshot group = service.AddTrophiesAsync(CurrentSeason, Grant("grant-skipped", skippedSeason, 40), CancellationToken.None).Result;

            Assert.AreEqual(laterSeason.SeasonId, group.Season.SeasonId);
            Assert.AreEqual(0, group.LocalTrophies, "Cúp của mùa bị nhảy qua không phải cúp của mùa hiện tại");
            Assert.DoesNotThrow(() => service.AddTrophiesAsync(CurrentSeason, Grant("grant-skipped", skippedSeason, 40), CancellationToken.None).Wait(),
                                "Gửi lại grant đã tính là idempotent");

            List<SeasonResult> results = ProcessAllPendingResults(service);
            CollectionAssert.AreEqual(new[] { heldSeason.SeasonId, skippedSeason.SeasonId }, results.Select(result => result.SeasonId));
            Assert.AreEqual(5, results[0].FinalTrophies);
            Assert.AreEqual(40, results[1].FinalTrophies);
            Assert.AreEqual(results[0].TierIndexAfter, results[1].TierIndexBefore, "Bậc của mùa bị nhảy qua = bậc mùa sau của mùa liền trước");
            Assert.AreEqual(results[1].TierIndexAfter, service.GetGroupAsync(CurrentSeason, CancellationToken.None).Result.TierIndex);
        }

        /// <summary>
        /// Trạng thái (b): một lượt gọi khác (mở trang, luồng kết quả mùa) đã đưa dịch vụ sang N+2 và khép N trước khi grant N+1 kịp
        /// gửi. Grant N+1 phải chen đúng chỗ giữa N và N+2, và kết quả của nó đổi bậc của N+2 (N+2 chưa có cúp).
        /// </summary>
        [Test]
        public void Contract_GrantOfSkippedSeason_AfterLaterSeasonAlreadyHeld_CountsForSkippedSeason_AndMovesTierOfEmptyHeldSeason()
        {
            ILeagueGroupService service = CreateService();
            SeasonWindow closedSeason = CurrentSeason;
            service.AddTrophiesAsync(closedSeason, Grant("grant-closed", closedSeason, 5), CancellationToken.None).Wait();
            SeasonWindow skippedSeason = SeasonAfter(closedSeason);
            SeasonWindow heldSeason = SeasonAfter(skippedSeason);

            MoveInto(heldSeason);
            Assume.That(service.GetGroupAsync(CurrentSeason, CancellationToken.None).Result.Season.SeasonId, Is.EqualTo(heldSeason.SeasonId));
            LeagueGroupSnapshot group = service.AddTrophiesAsync(CurrentSeason, Grant("grant-skipped", skippedSeason, 1000000), CancellationToken.None).Result;

            Assert.AreEqual(heldSeason.SeasonId, group.Season.SeasonId);
            Assert.AreEqual(0, group.LocalTrophies);
            Assert.AreEqual(1, group.TierIndex, "Mùa đang giữ chưa có cúp thì đổi bậc theo kết quả của mùa bị nhảy qua");

            List<SeasonResult> results = ProcessAllPendingResults(service);
            CollectionAssert.AreEqual(new[] { closedSeason.SeasonId, skippedSeason.SeasonId }, results.Select(result => result.SeasonId));
            Assert.AreEqual(5, results[0].FinalTrophies);
            Assert.AreEqual(1000000, results[1].FinalTrophies);
            Assert.AreEqual(SeasonOutcome.Promoted, results[1].Outcome);
            Assert.AreEqual(results[0].TierIndexAfter, results[1].TierIndexBefore);
        }

        /// <summary>
        /// A1: mùa đầu tiên dịch vụ từng giữ là mùa TRỐNG nằm sau mùa của grant — một lượt GetGroup ở mùa sau chạy xong trước khi grant
        /// kịp gửi (lượt gửi đầu hỏng mà trang vẫn tải, hoặc lượt gửi dùng chung bị huỷ). Grant phải được tính cho mùa của nó, y như khi
        /// dịch vụ chưa giữ mùa nào: nhận hay mất không được tuỳ thứ tự lượt gọi.
        /// </summary>
        [Test]
        public void Contract_GrantOfSkippedSeason_WhenFirstHeldSeasonIsLaterAndEmpty_CountsForSkippedSeason_AndMovesTierOfHeldSeason()
        {
            ILeagueGroupService service = CreateService();
            SeasonWindow skippedSeason = CurrentSeason;
            SeasonWindow heldSeason = SeasonAfter(skippedSeason);

            MoveInto(heldSeason);
            LeagueGroupSnapshot heldBefore = service.GetGroupAsync(CurrentSeason, CancellationToken.None).Result;
            Assume.That(heldBefore.Season.SeasonId, Is.EqualTo(heldSeason.SeasonId));
            LeagueGroupSnapshot group = service.AddTrophiesAsync(CurrentSeason, Grant("grant-skipped", skippedSeason, 1000000), CancellationToken.None).Result;
            service.AddTrophiesAsync(CurrentSeason, Grant("grant-skipped-second", skippedSeason, 7), CancellationToken.None).Wait();

            Assert.AreEqual(heldSeason.SeasonId, group.Season.SeasonId);
            Assert.AreEqual(0, group.LocalTrophies, "Cúp của mùa bị nhảy qua không phải cúp của mùa đang giữ");
            List<SeasonResult> results = ProcessAllPendingResults(service);
            CollectionAssert.AreEqual(new[] { skippedSeason.SeasonId }, results.Select(result => result.SeasonId));
            Assert.AreEqual(1000007, results[0].FinalTrophies, "Grant thứ hai của cùng mùa đi đường grant tới muộn");
            Assert.AreEqual(heldBefore.TierIndex, results[0].TierIndexBefore, "Không có mùa liền trước: bậc = bậc mùa trống phía sau đã bắt đầu");
            Assert.AreEqual(SeasonOutcome.Promoted, results[0].Outcome);
            Assert.AreEqual(results[0].TierIndexAfter, service.GetGroupAsync(CurrentSeason, CancellationToken.None).Result.TierIndex,
                            "Mùa đang giữ còn trống thì đổi bậc theo kết quả của mùa bị nhảy qua");
        }

        /// <summary>
        /// C: giữa mùa bị nhảy qua và mùa đang giữ còn một mùa TRỐNG đã mở rồi khép (gửi vẫn hỏng suốt mùa đó nhưng trang vẫn tải).
        /// Grant phải chen đúng chỗ; mùa trống phía sau được tính lại theo chuỗi bậc mới và bậc truyền tới mùa đang giữ.
        /// </summary>
        [Test]
        public void Contract_GrantOfSkippedSeason_WithEmptyClosedSeasonInBetween_CountsForSkippedSeason_AndRechainsTiers()
        {
            ILeagueGroupService service = CreateService();
            SeasonWindow closedSeason = CurrentSeason;
            service.AddTrophiesAsync(closedSeason, Grant("grant-closed", closedSeason, 5), CancellationToken.None).Wait();
            SeasonWindow skippedSeason = SeasonAfter(closedSeason);
            SeasonWindow emptySeason = SeasonAfter(skippedSeason);
            SeasonWindow heldSeason = SeasonAfter(emptySeason);

            MoveInto(emptySeason);
            service.GetGroupAsync(CurrentSeason, CancellationToken.None).Wait();
            MoveInto(heldSeason);
            Assume.That(service.GetGroupAsync(CurrentSeason, CancellationToken.None).Result.Season.SeasonId, Is.EqualTo(heldSeason.SeasonId));
            LeagueGroupSnapshot group = service.AddTrophiesAsync(CurrentSeason, Grant("grant-skipped", skippedSeason, 1000000), CancellationToken.None).Result;

            Assert.AreEqual(heldSeason.SeasonId, group.Season.SeasonId);
            Assert.AreEqual(0, group.LocalTrophies);
            List<SeasonResult> results = ProcessAllPendingResults(service);
            CollectionAssert.AreEqual(new[] { closedSeason.SeasonId, skippedSeason.SeasonId }, results.Select(result => result.SeasonId),
                                      "Mùa trống ở giữa không chơi, bậc không đổi theo luật mặc định → không có kết quả");
            Assert.AreEqual(5, results[0].FinalTrophies);
            Assert.AreEqual(1000000, results[1].FinalTrophies);
            Assert.AreEqual(SeasonOutcome.Promoted, results[1].Outcome);
            Assert.AreEqual(results[0].TierIndexAfter, results[1].TierIndexBefore, "Bậc = bậc mùa sau của mùa khép liền trước");
            Assert.AreEqual(results[1].TierIndexAfter, service.GetGroupAsync(CurrentSeason, CancellationToken.None).Result.TierIndex,
                            "Bậc truyền qua mùa trống đã khép tới mùa đang giữ");
        }

        /// <summary>
        /// Mùa phía sau mùa của grant đã có cúp: người chơi đã chơi ở bậc tính khi chưa có grant này, chèn sổ vào là đổi lịch sử.
        /// Grant bị từ chối <see cref="LeagueTrophyGrantRejection.UnknownSeason"/>, dữ liệu không đổi.
        /// </summary>
        [Test]
        public void Contract_GrantOfSkippedSeason_WhenLaterClosedSeasonHasTrophies_IsRejectedAsUnknown_AndNothingChanges()
        {
            ILeagueGroupService service = CreateService();
            SeasonWindow skippedSeason = CurrentSeason;
            SeasonWindow playedSeason = SeasonAfter(skippedSeason);
            SeasonWindow heldSeason = SeasonAfter(playedSeason);

            MoveInto(playedSeason);
            service.AddTrophiesAsync(CurrentSeason, Grant("grant-played", playedSeason, 8), CancellationToken.None).Wait();
            MoveInto(heldSeason);
            int heldTierBefore = service.GetGroupAsync(CurrentSeason, CancellationToken.None).Result.TierIndex;

            var failure = Assert.Throws<AggregateException>(() =>
                service.AddTrophiesAsync(CurrentSeason, Grant("grant-skipped", skippedSeason, 1000000), CancellationToken.None).Wait());
            var rejection = failure.InnerException as LeagueTrophyGrantRejectedException;
            Assert.IsNotNull(rejection);
            Assert.AreEqual(LeagueTrophyGrantRejection.UnknownSeason, rejection.Reason);

            List<SeasonResult> results = ProcessAllPendingResults(service);
            CollectionAssert.AreEqual(new[] { playedSeason.SeasonId }, results.Select(result => result.SeasonId));
            Assert.AreEqual(8, results[0].FinalTrophies);
            Assert.AreEqual(heldTierBefore, service.GetGroupAsync(CurrentSeason, CancellationToken.None).Result.TierIndex);
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

        private SimulatedLeagueGroupService Create(ILeagueTextStore store = null, SimulatedLeagueOptions options = null)
        {
            return new SimulatedLeagueGroupService(options ?? LeagueTestFactory.CreateSimulationOptions(), _rules, _clock,
                                                   store ?? new InMemoryLeagueTextStore());
        }

        private SeasonWindow SeasonNumber(int seasonNumber)
        {
            return _schedule.GetSeasonAt(LeagueTestFactory.Anchor + TimeSpan.FromTicks(LeagueTestFactory.SeasonLength.Ticks * seasonNumber) +
                                         TimeSpan.FromHours(1));
        }

        private static string ReadStored(InMemoryLeagueTextStore store)
        {
            Assert.IsTrue(store.TryRead(LeagueTestFactory.CreateSimulationOptions().StoreKey, out string text), "Dịch vụ phải đã lưu");
            return text;
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

        // ---------------------------------------------------------------- 0.2.1: kịch bản playtest + dữ liệu đã hỏng

        /// <summary>
        /// Đúng kịch bản playtest C2: cheat tua tới hết mùa, xem + nhận kết quả, tắt/mở Play (offset cheat không lưu → đồng hồ lùi
        /// về mùa cũ), thắng tiếp, rồi hết mùa lần nữa. Bản 0.2.0 mở lại mùa đã khép và sinh kết quả trùng mùa; luồng popup lặp mãi.
        /// </summary>
        [Test]
        public void PlaytestReplay_CheatSeasonEnd_ThenClockBackOnRestart_ThenSeasonEndsAgain_OneResultPerSeason()
        {
            var store = new InMemoryLeagueTextStore();
            SeasonWindow cheatedSeason = Season;
            SimulatedLeagueGroupService firstRun = Create(store);
            firstRun.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-1", cheatedSeason.SeasonId, 20), CancellationToken.None).Wait();

            _clock.Set(cheatedSeason.EndUtc + TimeSpan.FromMinutes(1));
            SeasonWindow nextSeason = Season;
            List<SeasonResult> firstFlow = LeagueSeasonFlowDriver.ProcessAllPendingResults(firstRun, () => Season);
            firstRun.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-2", nextSeason.SeasonId, 30), CancellationToken.None).Wait();
            CollectionAssert.AreEqual(new[] { cheatedSeason.SeasonId }, firstFlow.Select(result => result.SeasonId));

            // Mở lại Play: offset mất, đồng hồ về giữa mùa đã khép.
            _clock.Set(cheatedSeason.StartUtc + TimeSpan.FromHours(10));
            SimulatedLeagueGroupService secondRun = Create(store);
            LeagueGroupSnapshot group = secondRun.GetGroupAsync(Season, CancellationToken.None).Result;
            Assert.AreEqual(nextSeason.SeasonId, group.Season.SeasonId, "Không mở lại mùa đã khép");
            Assert.AreEqual(30, group.LocalTrophies);
            Assert.AreEqual(nextSeason.SeasonId, secondRun.ActiveSeasonId);

            // Thắng lúc đồng hồ đang lùi: grant gắn mùa đã chốt → bị từ chối có lý do, không đổi kết quả cũ.
            var failure = Assert.Throws<AggregateException>(() =>
                secondRun.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-3", cheatedSeason.SeasonId, 100), CancellationToken.None).Wait());
            Assert.AreEqual(LeagueTrophyGrantRejection.SeasonAlreadyFinalized, ((LeagueTrophyGrantRejectedException)failure.InnerException).Reason);

            // Cheat hết mùa lần nữa.
            _clock.Set(nextSeason.EndUtc + TimeSpan.FromMinutes(1));
            List<SeasonResult> secondFlow = LeagueSeasonFlowDriver.ProcessAllPendingResults(secondRun, () => Season);

            CollectionAssert.AreEqual(new[] { nextSeason.SeasonId }, secondFlow.Select(result => result.SeasonId));
            Assert.AreEqual(30, secondFlow[0].FinalTrophies);
            Assert.IsNull(secondRun.GetPendingResultAsync(Season, CancellationToken.None).Result);

            SimulatedLeagueData stored = SimulatedLeagueData.Decode(ReadStored(store));
            Assert.AreEqual(1, stored.ClosedSeasons.Count(closed => closed.SeasonId == cheatedSeason.SeasonId));
            Assert.AreEqual(1, stored.ClosedSeasons.Count(closed => closed.SeasonId == nextSeason.SeasonId));
            Assert.AreEqual(20, stored.FindClosedSeason(cheatedSeason.SeasonId).Result.FinalTrophies);
        }

        /// <summary>
        /// Save định dạng 1 của 0.2.0, đúng kiểu đã hỏng trên máy dev/QA: mùa đang giữ season-36 nằm ở TƯƠNG LAI (offset cheat chỉ
        /// sống trong RAM nên bị mất), hai kết quả season-35 (bản gốc đã xem + đã nhận, bản trùng còn chờ). Định dạng 1 bị coi là
        /// không dùng được: bắt đầu lại từ đầu, không ném lỗi, thắng trong mùa theo đồng hồ được tính.
        /// </summary>
        [Test]
        public void Decode_Format1SaveFrom020_StartsFresh_WithoutThrowing_AndWinsCount()
        {
            SeasonWindow season35 = SeasonNumber(35);
            SeasonWindow season36 = SeasonNumber(36);
            var chest = new LeagueRewardPackage(LeagueTestFactory.RegularChest, new[] { new LeagueRewardItem("coin", 10) });
            var format1 = new LeagueTextRecord(1);
            format1.SetInt("tier", 2);
            format1.SetString("active.season", season36.SeasonId);
            format1.SetLong("active.start", season36.StartUtc.Ticks);
            format1.SetLong("active.end", season36.EndUtc.Ticks);
            format1.SetInt("active.tier", 2);
            format1.SetLong("active.trophies", 70);
            format1.SetInt("active.grants", 0);
            format1.SetInt("results", 2);
            format1.SetResult("result0", new SeasonResult(season35.SeasonId, 0, 0, SeasonOutcome.Unchanged, 13, 30, 150, chest,
                                                          acknowledged: true, rewardClaimed: true));
            format1.SetResult("result1", new SeasonResult(season35.SeasonId, 0, 1, SeasonOutcome.Promoted, 2, 30, 250, chest,
                                                          acknowledged: false, rewardClaimed: false));
            var store = new InMemoryLeagueTextStore();
            store.Write(LeagueTestFactory.CreateSimulationOptions().StoreKey, format1.Encode());
            _clock.Set(season35.StartUtc + TimeSpan.FromDays(3));

            Assert.IsNull(SimulatedLeagueData.Decode(format1.Encode()), "Định dạng 1 không dùng được → bắt đầu lại");

            SimulatedLeagueGroupService service = null;
            Assert.DoesNotThrow(() => service = Create(store));
            Assert.IsNull(service.ActiveSeasonId);
            Assert.AreEqual(0, service.CurrentTierIndex, "Bắt đầu lại ở bậc khởi đầu");

            LeagueGroupSnapshot group = service.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-after-upgrade", season35, 20), CancellationToken.None).Result;
            Assert.AreEqual(season35.SeasonId, group.Season.SeasonId, "Không kẹt ở mùa tương lai của save cũ");
            Assert.AreEqual(20, group.LocalTrophies);
            Assert.IsNull(service.GetPendingResultAsync(Season, CancellationToken.None).Result, "Không còn kết quả trùng của save cũ");

            SimulatedLeagueData stored = SimulatedLeagueData.Decode(ReadStored(store));
            Assert.IsNotNull(stored, "Lần lưu kế tiếp ghi định dạng hiện tại");
            Assert.AreEqual(0, stored.ClosedSeasons.Count);
        }

        [Test]
        public void ClosedSeasonLedger_SurvivesRestart_ResendOfCountedGrantStaysIdempotent()
        {
            var store = new InMemoryLeagueTextStore();
            SeasonWindow oldSeason = Season;
            SimulatedLeagueGroupService firstRun = Create(store);
            firstRun.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-a", oldSeason.SeasonId, 25), CancellationToken.None).Wait();
            _clock.Set(oldSeason.EndUtc + TimeSpan.FromHours(2));
            SeasonResult closedResult = firstRun.GetPendingResultAsync(Season, CancellationToken.None).Result;

            // App bị tắt trước khi hàng chờ kịp xoá grant: lần mở sau gửi lại grant đó.
            SimulatedLeagueGroupService reopened = Create(store);
            reopened.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-a", oldSeason.SeasonId, 25), CancellationToken.None).Wait();

            SeasonResult afterResend = reopened.GetPendingResultAsync(Season, CancellationToken.None).Result;
            Assert.AreEqual(closedResult.ToString(), afterResend.ToString());

            SimulatedClosedSeason ledger = SimulatedLeagueData.Decode(ReadStored(store)).FindClosedSeason(oldSeason.SeasonId);
            Assert.AreEqual(oldSeason, ledger.Window);
            CollectionAssert.AreEquivalent(new[] { "grant-a" }, ledger.AppliedGrantIds);
            Assert.AreEqual(25, ledger.LocalTrophies);
        }

        [Test]
        public void LateGrant_WhenNextSeasonAlreadyHasTrophies_UpdatesResultButKeepsCurrentTier()
        {
            SimulatedLeagueGroupService service = Create();
            SeasonWindow oldSeason = Season;
            service.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-a", oldSeason.SeasonId, 1), CancellationToken.None).Wait();
            _clock.Set(oldSeason.EndUtc + TimeSpan.FromHours(1));
            SeasonWindow nextSeason = Season;
            service.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-b", nextSeason.SeasonId, 5), CancellationToken.None).Wait();

            service.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-late", oldSeason.SeasonId, 1000000), CancellationToken.None).Wait();

            SeasonResult result = service.GetPendingResultAsync(Season, CancellationToken.None).Result;
            Assert.AreEqual(SeasonOutcome.Promoted, result.Outcome);
            Assert.AreEqual(1, result.TierIndexAfter);
            Assert.AreEqual(0, service.CurrentTierIndex, "Đã kiếm cúp ở bậc cũ trong mùa này thì không đổi bậc giữa mùa");
            Assert.AreEqual(5, service.LocalTrophies);
        }

        [Test]
        public void GrantForSeasonNeverHeld_IsRejectedAsUnknown_WithoutTouchingHeldSeason()
        {
            SimulatedLeagueGroupService service = Create();
            SeasonWindow heldSeason = Season;
            service.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-a", heldSeason, 8), CancellationToken.None).Wait();
            SeasonWindow olderSeason = _schedule.GetSeasonAt(heldSeason.StartUtc - TimeSpan.FromDays(1));

            // Mang cửa sổ mùa cũng không nhận: mùa đang giữ (phía sau mùa của grant) đã có cúp — người chơi đã chơi ở bậc tính khi
            // chưa có grant này. Mùa đang giữ còn trống thì nhận (xem contract test mùa đầu tiên dịch vụ giữ là mùa trống phía sau).
            var failure = Assert.Throws<AggregateException>(() =>
                service.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-old", olderSeason, 50), CancellationToken.None).Wait());

            Assert.AreEqual(LeagueTrophyGrantRejection.UnknownSeason, ((LeagueTrophyGrantRejectedException)failure.InnerException).Reason);
            Assert.AreEqual(8, service.LocalTrophies);
            Assert.AreEqual(heldSeason.SeasonId, service.ActiveSeasonId);
        }

        /// <summary>Grant chỉ có id mùa (không cửa sổ) của mùa bị nhảy qua: dịch vụ không biết mùa đó ở đâu nên từ chối có lý do.</summary>
        [Test]
        public void SkippedSeasonGrant_WithoutSeasonWindow_IsRejectedAsUnknown()
        {
            SimulatedLeagueGroupService service = Create();
            SeasonWindow heldSeason = Season;
            service.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-a", heldSeason, 8), CancellationToken.None).Wait();
            SeasonWindow skippedSeason = _schedule.GetSeasonAt(heldSeason.EndUtc);
            _clock.Set(skippedSeason.EndUtc + TimeSpan.FromHours(1));

            var failure = Assert.Throws<AggregateException>(() =>
                service.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-skipped", skippedSeason.SeasonId, 50), CancellationToken.None).Wait());

            Assert.AreEqual(LeagueTrophyGrantRejection.UnknownSeason, ((LeagueTrophyGrantRejectedException)failure.InnerException).Reason);
        }

        [Test]
        public void SkippedSeasonGrant_ManyEmptySeasonsSkipped_OnlySeasonWithGrantGetsLedger()
        {
            var store = new InMemoryLeagueTextStore();
            SimulatedLeagueGroupService service = Create(store);
            SeasonWindow heldSeason = Season;
            service.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-held", heldSeason, 8), CancellationToken.None).Wait();
            SeasonWindow emptyBefore = _schedule.GetSeasonAt(heldSeason.EndUtc);
            SeasonWindow skippedSeason = _schedule.GetSeasonAt(emptyBefore.EndUtc);
            SeasonWindow emptyAfter = _schedule.GetSeasonAt(skippedSeason.EndUtc);
            SeasonWindow laterSeason = _schedule.GetSeasonAt(emptyAfter.EndUtc);

            _clock.Set(laterSeason.StartUtc + TimeSpan.FromHours(1));
            service.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-skipped", skippedSeason, 30), CancellationToken.None).Wait();

            SimulatedLeagueData stored = SimulatedLeagueData.Decode(ReadStored(store));
            CollectionAssert.AreEqual(new[] { heldSeason.SeasonId, skippedSeason.SeasonId }, stored.ClosedSeasons.Select(closed => closed.SeasonId));
            Assert.AreEqual(laterSeason.SeasonId, stored.ActiveSeasonId);
            Assert.AreEqual(30, stored.FindClosedSeason(skippedSeason.SeasonId).Result.FinalTrophies);
            Assert.AreEqual(skippedSeason, stored.FindClosedSeason(skippedSeason.SeasonId).Window);
        }

        /// <summary>
        /// Lượt gọi mang mùa chưa bắt đầu theo đồng hồ của dịch vụ (lượt gọi cũ đọc giờ đã tua): bị từ chối bằng lỗi tạm, không mở mùa
        /// tương lai, không khép sớm mùa đang giữ, không cộng cúp.
        /// </summary>
        [Test]
        public void CallCarryingSeasonNotStartedByServiceClock_IsTransientError_AndHeldSeasonUntouched()
        {
            SimulatedLeagueGroupService service = Create();
            SeasonWindow heldSeason = Season;
            service.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-a", heldSeason, 8), CancellationToken.None).Wait();
            SeasonWindow futureSeason = _schedule.GetSeasonAt(heldSeason.EndUtc);

            var groupFailure = Assert.Throws<AggregateException>(() => service.GetGroupAsync(futureSeason, CancellationToken.None).Wait());
            Assert.IsInstanceOf<SimulatedLeagueException>(groupFailure.InnerException);
            var addFailure = Assert.Throws<AggregateException>(() =>
                service.AddTrophiesAsync(futureSeason, new LeagueTrophyGrant("grant-future", futureSeason, 5), CancellationToken.None).Wait());
            Assert.IsInstanceOf<SimulatedLeagueException>(addFailure.InnerException, "Lỗi tạm, không phải từ chối dứt khoát: grant còn nằm hàng chờ");

            Assert.AreEqual(heldSeason.SeasonId, service.ActiveSeasonId);
            Assert.AreEqual(8, service.LocalTrophies);
            Assert.IsNull(service.GetPendingResultAsync(Season, CancellationToken.None).Result, "Mùa đang giữ không bị khép sớm");
        }

        /// <summary>
        /// Playtest: cheat tua sang mùa sau, một lượt gọi bắt đầu (độ trễ mạng) mang mùa đó, rồi cheat xoá dữ liệu (offset về 0) trước
        /// khi lượt đó về. Lượt cũ phải thất bại và không ghi gì — trước đây nó mở mùa tương lai trên dữ liệu vừa xoá và League kẹt ở
        /// đó, mọi trận thắng bị từ chối kể cả sau khi mở lại app. Thất bại bằng lỗi tạm <see cref="SimulatedLeagueException"/>, KHÔNG
        /// bằng <see cref="OperationCanceledException"/>: token của nơi gọi không bị huỷ, widget coi huỷ là "người dùng huỷ" sẽ đứng
        /// mãi ở trạng thái đang tải.
        /// </summary>
        [Test]
        public void InFlightCallStartedBeforeReset_FailsAsTransientError_AndDoesNotPinFutureSeason()
        {
            const int latencyMilliseconds = 200;
            var store = new InMemoryLeagueTextStore();
            SimulatedLeagueOptions options = LeagueTestFactory.CreateSimulationOptions();
            SimulatedLeagueGroupService service = Create(store, options);
            SeasonWindow realSeason = Season;
            service.GetGroupAsync(realSeason, CancellationToken.None).Wait();

            _clock.Set(realSeason.EndUtc + TimeSpan.FromHours(1));
            SeasonWindow cheatedSeason = Season;
            service.LatencyMilliseconds = latencyMilliseconds;
            Task<LeagueGroupSnapshot> staleCall = StartWithoutSynchronizationContext(() => service.GetGroupAsync(cheatedSeason, CancellationToken.None));

            // Cheat xoá dữ liệu trong lúc lượt gọi còn chờ độ trễ.
            _clock.Set(realSeason.StartUtc + TimeSpan.FromDays(2));
            service.DebugResetSimulation();

            var failure = Assert.Throws<AggregateException>(() => staleCall.Wait());
            Assert.IsInstanceOf<SimulatedLeagueException>(failure.InnerException);
            Assert.IsNotInstanceOf<OperationCanceledException>(failure.InnerException);
            Assert.IsFalse(store.TryRead(options.StoreKey, out _), "Lượt gọi bị bỏ không được ghi lên dữ liệu vừa xoá");
            Assert.IsNull(service.ActiveSeasonId);

            service.LatencyMilliseconds = 0;
            LeagueGroupSnapshot group = service.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-after-reset", realSeason, 12), CancellationToken.None).Result;
            Assert.AreEqual(realSeason.SeasonId, group.Season.SeasonId, "Không kẹt ở mùa tương lai");
            Assert.AreEqual(12, group.LocalTrophies);

            SimulatedLeagueGroupService reopened = Create(store, options);
            Assert.AreEqual(realSeason.SeasonId, reopened.ActiveSeasonId, "Mở lại app vẫn đúng mùa theo đồng hồ");
        }

        /// <summary>
        /// Mùa trống đã khép phía sau mùa bị nhảy qua được tính lại theo bậc mới: với luật "không chơi thì xuống một bậc", mùa trống ở
        /// bậc thấp nhất không có kết quả, đổi lên bậc trên thì kết quả xuống hạng xuất hiện và bậc mùa đang giữ theo kết quả đó.
        /// </summary>
        [Test]
        public void SkippedSeasonGrant_RechainedEmptyClosedSeason_ResultAppearsWhenNewTierChangesOutcome()
        {
            LeagueRules rules = LeagueTestFactory.CreateRules(new InactiveDemotesOneTierOutcomeRule());
            var service = new SimulatedLeagueGroupService(LeagueTestFactory.CreateSimulationOptions(), rules, _clock, new InMemoryLeagueTextStore());
            SeasonWindow skippedSeason = Season;
            SeasonWindow emptySeason = _schedule.GetSeasonAt(skippedSeason.EndUtc);
            SeasonWindow heldSeason = _schedule.GetSeasonAt(emptySeason.EndUtc);

            _clock.Set(emptySeason.StartUtc + TimeSpan.FromHours(1));
            service.GetGroupAsync(Season, CancellationToken.None).Wait();
            _clock.Set(heldSeason.StartUtc + TimeSpan.FromHours(1));
            service.GetGroupAsync(Season, CancellationToken.None).Wait();
            Assume.That(service.GetPendingResultAsync(Season, CancellationToken.None).Result, Is.Null, "Mùa trống ở bậc thấp nhất: không kết quả");

            service.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-skipped", skippedSeason, 1000000), CancellationToken.None).Wait();

            List<SeasonResult> results = LeagueSeasonFlowDriver.ProcessAllPendingResults(service, () => Season);
            CollectionAssert.AreEqual(new[] { skippedSeason.SeasonId, emptySeason.SeasonId }, results.Select(result => result.SeasonId));
            Assert.AreEqual(SeasonOutcome.Promoted, results[0].Outcome);
            Assert.AreEqual(results[0].TierIndexAfter, results[1].TierIndexBefore);
            Assert.AreEqual(SeasonOutcome.Demoted, results[1].Outcome, "Mùa trống giờ ở bậc trên, không chơi → xuống một bậc");
            Assert.AreEqual(0, results[1].FinalTrophies);
            Assert.AreEqual(results[1].TierIndexAfter, service.CurrentTierIndex);
        }

        /// <summary>
        /// Ngược lại: mùa trống đã khép có kết quả xuống hạng (được đặt ở bậc trên), mùa bị nhảy qua phía trước cho ra bậc thấp nhất
        /// → mùa trống tính lại ở bậc thấp nhất, kết quả biến mất (chưa ai xem nên chưa chốt).
        /// </summary>
        [Test]
        public void SkippedSeasonGrant_RechainedEmptyClosedSeason_ResultDisappearsWhenNewTierChangesOutcome()
        {
            LeagueRules rules = LeagueTestFactory.CreateRules(new InactiveDemotesOneTierOutcomeRule());
            SimulatedLeagueOptions options = LeagueTestFactory.CreateSimulationOptions();
            options.IdleBotShare = 0;
            options.LatestBotStartProgress = 0;
            var service = new SimulatedLeagueGroupService(options, rules, _clock, new InMemoryLeagueTextStore());
            SeasonWindow skippedSeason = Season;
            SeasonWindow emptySeason = _schedule.GetSeasonAt(skippedSeason.EndUtc);
            SeasonWindow heldSeason = _schedule.GetSeasonAt(emptySeason.EndUtc);

            _clock.Set(emptySeason.StartUtc + TimeSpan.FromHours(1));
            service.DebugSetTierIndex(Season, 1);
            _clock.Set(heldSeason.StartUtc + TimeSpan.FromHours(1));
            SeasonResult emptyResultBefore = service.GetPendingResultAsync(Season, CancellationToken.None).Result;
            Assume.That(emptyResultBefore, Is.Not.Null);
            Assume.That(emptyResultBefore.SeasonId, Is.EqualTo(emptySeason.SeasonId));

            // Bậc của mùa bị nhảy qua = bậc mùa trống đã bắt đầu (1); 1 cúp khi không bot nào vắng mặt = hạng cuối → xuống bậc 0.
            service.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-skipped", skippedSeason, 1), CancellationToken.None).Wait();

            List<SeasonResult> results = LeagueSeasonFlowDriver.ProcessAllPendingResults(service, () => Season);
            CollectionAssert.AreEqual(new[] { skippedSeason.SeasonId }, results.Select(result => result.SeasonId),
                                      "Mùa trống giờ ở bậc thấp nhất: không chơi không xuống được nữa → không kết quả");
            Assert.AreEqual(1, results[0].TierIndexBefore);
            Assert.AreEqual(SeasonOutcome.Demoted, results[0].Outcome);
            Assert.AreEqual(0, service.CurrentTierIndex);
        }

        /// <summary>
        /// Grant tới muộn cho mùa đã khép đổi bậc mùa sau: bậc truyền qua cả sổ trống đã khép phía sau tới mùa đang giữ còn trống —
        /// trước đây chỉ truyền khi mùa đó là sổ khép gần nhất, nên cùng một grant cho ra bậc khác nhau tuỳ lúc mùa trống bị khép.
        /// </summary>
        [Test]
        public void LateGrant_WithEmptyClosedSeasonAfter_PropagatesTierThroughEmptySeasonsToHeldSeason()
        {
            SimulatedLeagueGroupService service = Create();
            SeasonWindow closedSeason = Season;
            service.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-a", closedSeason, 5), CancellationToken.None).Wait();
            SeasonWindow emptySeason = _schedule.GetSeasonAt(closedSeason.EndUtc);
            SeasonWindow heldSeason = _schedule.GetSeasonAt(emptySeason.EndUtc);
            _clock.Set(emptySeason.StartUtc + TimeSpan.FromHours(1));
            service.GetGroupAsync(Season, CancellationToken.None).Wait();
            _clock.Set(heldSeason.StartUtc + TimeSpan.FromHours(1));
            service.GetGroupAsync(Season, CancellationToken.None).Wait();
            SeasonResult before = service.GetPendingResultAsync(Season, CancellationToken.None).Result;
            Assume.That(before.Outcome, Is.EqualTo(SeasonOutcome.Unchanged), "5 cúp phải chưa đủ lên hạng");

            service.AddTrophiesAsync(Season, new LeagueTrophyGrant("grant-late", closedSeason, 1000000), CancellationToken.None).Wait();

            List<SeasonResult> results = LeagueSeasonFlowDriver.ProcessAllPendingResults(service, () => Season);
            CollectionAssert.AreEqual(new[] { closedSeason.SeasonId }, results.Select(result => result.SeasonId));
            Assert.AreEqual(SeasonOutcome.Promoted, results[0].Outcome);
            Assert.AreEqual(results[0].TierIndexAfter, service.CurrentTierIndex);
            Assert.AreEqual(heldSeason.SeasonId, service.ActiveSeasonId);
        }

        /// <summary>Luật thử: người không chơi cả mùa xuống một bậc (bậc thấp nhất thì giữ) — kết quả của mùa trống phụ thuộc bậc.</summary>
        private sealed class InactiveDemotesOneTierOutcomeRule : ISeasonOutcomeRule
        {
            private readonly ZoneSeasonOutcomeRule _zoneRule = new ZoneSeasonOutcomeRule();

            public SeasonOutcomeDecision Decide(in SeasonOutcomeInput input, LeagueLadder ladder, ILeagueZoneRule zoneRule)
            {
                if (input.Participated || ladder.IsBottom(input.TierIndex)) return _zoneRule.Decide(input, ladder, zoneRule);
                return new SeasonOutcomeDecision(SeasonOutcome.Demoted, input.TierIndex - 1);
            }
        }

        /// <summary>
        /// Chạy lượt gọi không có SynchronizationContext: continuation sau độ trễ chạy trên thread pool, nên test chờ đồng bộ được mà
        /// không kẹt main thread của Editor.
        /// </summary>
        private static Task<T> StartWithoutSynchronizationContext<T>(Func<Task<T>> start)
        {
            SynchronizationContext previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                return start();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }
    }
}
