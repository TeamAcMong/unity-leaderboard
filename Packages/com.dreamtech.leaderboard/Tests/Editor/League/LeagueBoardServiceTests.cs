using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DreamTech.Leaderboard.League.Tests
{
    /// <summary>
    /// <see cref="LeagueBoardService"/> là chỗ nối League với bộ hiển thị leaderboard. Nếu nó sai thì trang League vẫn hiện
    /// nhưng không bao giờ diễn lên hạng — đúng lỗi "toàn là tĩnh" — nên test đi hết đường qua <see cref="LeaderboardBoard"/>.
    /// </summary>
    [TestFixture]
    public class LeagueBoardServiceTests
    {
        private static readonly LevelWinContext NormalWin = new LevelWinContext(12, 0);

        /// <summary>
        /// Lượt gọi không bị chặn (không chờ phản hồi nào đang treo) xong gần như ngay; quá chừng này nghĩa là nó đang chờ một phản hồi
        /// không tới — chờ có hạn để test báo sai thay vì treo.
        /// </summary>
        private static readonly TimeSpan UnblockedCallTimeout = TimeSpan.FromSeconds(5);

        private const string ClockHighWaterKey = "clock.highWater";

        /// <summary>Giờ máy bị chỉnh lùi chừng này so với mốc của đồng hồ không-lùi (vd sau khi tua giờ để hồi mạng rồi chỉnh lại).</summary>
        private static readonly TimeSpan DeviceClockSetBack = TimeSpan.FromHours(1);

        [Test]
        public void GetRange_ReturnsStandingsSlice_AndSeasonKeyIsSeasonId()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            var service = new LeagueBoardService(scenario.System);

            IReadOnlyList<LeaderboardEntry> range = service.GetRangeAsync(5, 4, CancellationToken.None).Result;
            LeaderboardEntry local = service.GetLocalEntryAsync(CancellationToken.None).Result;

            Assert.AreEqual(4, range.Count);
            for (int index = 0; index < range.Count; index++) Assert.AreEqual(5 + index, range[index].Rank);
            Assert.IsNotNull(local);
            Assert.AreEqual(scenario.System.CurrentSeason.SeasonId, service.SeasonKey);
            Assert.AreEqual(scenario.Simulation.LocalPlayerId, service.LocalPlayerId);
        }

        [Test]
        public void GetRange_PastEndOrEmptyLimit_ReturnsEmpty()
        {
            var service = new LeagueBoardService(LeagueScenario.Create().System);

            Assert.AreEqual(0, service.GetRangeAsync(1000, 10, CancellationToken.None).Result.Count);
            Assert.AreEqual(0, service.GetRangeAsync(0, 0, CancellationToken.None).Result.Count);
            Assert.AreEqual(3, service.GetRangeAsync(27, 10, CancellationToken.None).Result.Count, "Nhóm 30 người, từ hạng 27 còn 3");
        }

        [Test]
        public void ConcurrentQueries_HitGroupServiceOnce()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            var service = new LeagueBoardService(scenario.System);

            // Đúng kiểu LoadSceneAsync hỏi: entry của mình + top + cửa sổ, gần như cùng lúc.
            Task.WhenAll(service.GetLocalEntryAsync(CancellationToken.None),
                         service.GetRangeAsync(0, 10, CancellationToken.None),
                         service.GetRangeAsync(10, 10, CancellationToken.None)).Wait();

            Assert.AreEqual(1, scenario.Service.GetGroupCallCount);
        }

        [Test]
        public void TryGetScore_IsServiceTrophiesPlusUnsent()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            var service = new LeagueBoardService(scenario.System);
            Assert.IsFalse(service.TryGetScore(out _), "Chưa tải bảng thì chưa biết điểm");

            LeaderboardEntry before = service.GetLocalEntryAsync(CancellationToken.None).Result;
            scenario.System.RecordLevelWin(NormalWin);

            Assert.IsTrue(service.TryGetScore(out long score));
            Assert.AreEqual(before.Score + 10, score);
        }

        [Test]
        public void SubmitScore_IgnoresValue_FlushesQueue_ReturnsFreshEntry()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            var service = new LeagueBoardService(scenario.System);
            LeaderboardEntry before = service.GetLocalEntryAsync(CancellationToken.None).Result;
            scenario.System.RecordLevelWin(NormalWin);
            scenario.System.RecordLevelWin(NormalWin);

            // Con số truyền vào vô nghĩa: League không cho client tự đặt điểm.
            LeaderboardEntry after = service.SubmitScoreAsync(999999, CancellationToken.None).Result;

            Assert.AreEqual(before.Score + 20, after.Score);
            Assert.AreEqual(0, scenario.System.UnsentTrophies);
            Assert.AreEqual(2, scenario.Service.AddTrophiesCallCount);
        }

        /// <summary>
        /// Lượt tải cùng mùa đang bay (vd HUD) đã hỏi bảng TRƯỚC khi grant tới dịch vụ; người chơi thắng, board thấy điểm lệch và gửi
        /// điểm. <see cref="LeagueBoardService.SubmitScoreAsync"/> gửi cúp xong không được nhập vào lượt đó — entry trả về sẽ thiếu grant
        /// vừa gửi và phải chờ phản hồi của lượt cũ — mà phải mở lượt mới.
        /// </summary>
        [Test]
        public void SubmitScore_DoesNotJoinLoadOpenedBeforeGrantsWereSent_ReturnsEntryWithSentGrants()
        {
            WithoutSynchronizationContext.Run(() =>
            {
                LeagueScenario scenario = LeagueScenario.Create();
                var service = new LeagueBoardService(scenario.System);
                LeaderboardEntry before = service.GetLocalEntryAsync(CancellationToken.None).Result;

                // Lượt tải mở trước khi thắng: dịch vụ đã dựng bảng (chưa có grant), phản hồi về muộn.
                service.Invalidate();
                var earlierLoadResponse = new TaskCompletionSource<bool>();
                scenario.Service.GetGroupResponseGate = earlierLoadResponse;
                Task<LeaderboardEntry> earlierLoad = service.GetLocalEntryAsync(CancellationToken.None);
                scenario.Service.GetGroupResponseGate = null;
                Assume.That(earlierLoad.IsCompleted, Is.False, "Lượt tải mở trước phải còn đang bay");
                int groupCallsBeforeSubmit = scenario.Service.GetGroupCallCount;

                int won = scenario.System.RecordLevelWin(NormalWin).Trophies;
                Task<LeaderboardEntry> submitted = service.SubmitScoreAsync(0, CancellationToken.None);

                Assert.IsTrue(submitted.Wait(UnblockedCallTimeout), "Gửi điểm không được chờ phản hồi của lượt tải mở trước khi gửi cúp");
                Assert.AreEqual(before.Score + won, submitted.Result.Score, "Entry trả về phải có grant vừa gửi");
                Assert.AreEqual(groupCallsBeforeSubmit + 1, scenario.Service.GetGroupCallCount, "Gửi điểm mở lượt tải mới");
                LeagueGroupSnapshot afterSubmit = service.LastKnownSnapshot;

                earlierLoadResponse.SetResult(true);
                Assert.AreEqual(before.Score, earlierLoad.Result.Score, "Người gọi của lượt cũ vẫn nhận bảng lượt đó đã hỏi");
                Assert.AreSame(afterSubmit, service.LastKnownSnapshot, "Phản hồi muộn của lượt cũ không ghi đè snapshot mới hơn");
                Assert.IsTrue(service.TryGetScore(out long score));
                Assert.AreEqual(before.Score + won, score);
            });
        }

        /// <summary>
        /// Giờ máy chậm hơn mốc của <see cref="MonotonicLeagueClock"/>: giờ League đứng yên ở mốc. Độ tươi của snapshot phải đo thêm
        /// bằng thời gian thực — trước đây chỉ đo bằng giờ League nên hiệu giờ bằng 0 mãi, snapshot "tươi" suốt hàng giờ và mở trang bao
        /// nhiêu lần cũng không gọi lại dịch vụ. Nguồn thời gian thực được cắm tay để test không phải ngủ.
        /// </summary>
        [Test]
        public void LeagueClockFrozenAtHighWater_SnapshotStillExpiresByRealTime()
        {
            ManualLeagueClock deviceClock = LeagueTestFactory.CreateClockInFirstSeason();
            var store = new InMemoryLeagueTextStore();
            var clock = new MonotonicLeagueClock(deviceClock, store, ClockHighWaterKey);
            DateTime highWaterUtc = clock.UtcNow;
            deviceClock.Set(highWaterUtc - DeviceClockSetBack);
            Assume.That(clock.IsInnerBehind, Is.True);

            LeagueRules rules = LeagueTestFactory.CreateRules();
            var simulation = new SimulatedLeagueGroupService(LeagueTestFactory.CreateSimulationOptions(), rules, clock, store);
            var countingService = new CountingLeagueGroupService(simulation);
            LeagueSystem system = new LeagueSystemBuilder(LeagueScenario.SystemId, rules, LeagueTestFactory.CreateStreakLadder())
                                  .WithGroupService(countingService)
                                  .WithSchedule(LeagueTestFactory.CreateSchedule())
                                  .WithClock(clock)
                                  .WithTextStore(store)
                                  .WithRewardGranter(new RecordingLeagueRewardGranter())
                                  .WithFeatureGate(new ManualLeagueFeatureGate(true))
                                  .WithTrophyRule(new MultipliedTrophyRule(new[] { 10, 15, 20 }))
                                  .Build();
            TimeSpan realTime = TimeSpan.Zero;
            var service = new LeagueBoardService(system, () => realTime);
            LeaderboardBoard board = CreateBoard(service);

            board.LoadSceneAsync(BoardPresentMode.Browse, CancellationToken.None).Wait();
            Assume.That(countingService.GetGroupCallCount, Is.EqualTo(1));

            // Thời gian thực còn trong độ tươi: mở lại dùng bảng trong cache.
            TimeSpan withinFreshness = TimeSpan.FromTicks(LeagueBoardService.SnapshotFreshness.Ticks / 2);
            realTime += withinFreshness;
            board.LoadSceneAsync(BoardPresentMode.Browse, CancellationToken.None).Wait();
            Assert.AreEqual(1, countingService.GetGroupCallCount, "Còn tươi thì không gọi lại dịch vụ");

            // Thời gian thực trôi quá độ tươi; giờ máy tiến theo nhưng vẫn chậm hơn mốc nên giờ League không đổi.
            realTime += LeagueBoardService.SnapshotFreshness;
            deviceClock.Advance(LeagueBoardService.SnapshotFreshness);
            Assume.That(clock.UtcNow, Is.EqualTo(highWaterUtc), "Giờ League phải đứng yên ở mốc");
            board.LoadSceneAsync(BoardPresentMode.Browse, CancellationToken.None).Wait();

            Assert.AreEqual(2, countingService.GetGroupCallCount, "Thời gian thực đã quá độ tươi: phải gọi lại dịch vụ dù giờ League đứng yên");
        }

        [Test]
        public void LeagueStateChange_RaisesScoreChanged()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            var service = new LeagueBoardService(scenario.System);
            int raised = 0;
            service.ScoreChanged += () => raised++;

            scenario.System.RecordLevelWin(NormalWin);
            service.Dispose();
            scenario.System.RecordLevelWin(NormalWin);

            Assert.AreEqual(1, raised, "Dispose phải gỡ đăng ký");
        }

        [Test]
        public void SeasonRoll_ChangesSeasonKey_AndRefetches()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            var service = new LeagueBoardService(scenario.System);
            _ = service.GetLocalEntryAsync(CancellationToken.None).Result;
            string firstKey = service.SeasonKey;

            scenario.AdvancePastSeasonEnd();
            _ = service.GetLocalEntryAsync(CancellationToken.None).Result;

            Assert.AreNotEqual(firstKey, service.SeasonKey);
            Assert.AreEqual(2, scenario.Service.GetGroupCallCount, "Snapshot mùa cũ không được dùng cho mùa mới");
        }

        [Test]
        public void ThroughLeaderboardBoard_WinsAfterLastView_RevealAsRankUp()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            var service = new LeagueBoardService(scenario.System);
            var board = new LeaderboardBoard(new LeaderboardBoardSettings("league", new FetchWindowSettings(50, 4, 6, 15), new RankTierRule(3)),
                                             service, service, new InMemoryLeaderboardSnapshotStore());

            // Lần mở đầu mùa: xem xong thì ghi nhận là đã xem.
            BoardScene first = board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result;
            board.MarkRevealed(first.Change);
            int rankBefore = first.Change.ToRank;
            Assume.That(rankBefore, Is.GreaterThan(0), "Seed phải để người chơi không đứng nhất sẵn");

            // Thắng liền mấy màn (streak nhân cúp) cho chắc chắn vượt ít nhất một người.
            for (int index = 0; index < 12; index++) scenario.System.RecordLevelWin(NormalWin);
            Assert.IsTrue(board.HasUnrevealedChange, "Có cúp chưa gửi thì phải báo có thay đổi chưa xem");

            BoardScene second = board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result;

            Assert.AreEqual(RankChangeKind.RankUp, second.Change.Kind);
            Assert.AreEqual(rankBefore, second.Change.FromRank);
            Assert.Less(second.Change.ToRank, rankBefore);
            Assert.AreEqual(0, scenario.System.UnsentTrophies, "Mở trang là đẩy hết cúp chờ");
            Assert.AreEqual(second.Change.ToRank, second.Rows[second.LocalRowIndex].Entry.Rank);
        }

        /// <summary>
        /// Mở trang League ngay sau khi hết mùa, lúc còn cúp mùa cũ trong hàng chờ: đường GetLocalEntry → bảng không được là thứ
        /// khép mùa cũ trước khi cúp tới — cúp phải được gửi TRƯỚC lượt hỏi bảng mùa mới.
        /// </summary>
        [Test]
        public void OpeningBoardAfterSeasonEnd_SendsQueuedOldSeasonTrophies_BeforeAskingForNewSeasonGroup()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            var service = new LeagueBoardService(scenario.System);
            _ = service.GetLocalEntryAsync(CancellationToken.None).Result;
            int queued = scenario.System.RecordLevelWin(NormalWin).Trophies + scenario.System.RecordLevelWin(NormalWin).Trophies;
            string oldSeasonId = scenario.System.CurrentSeason.SeasonId;

            scenario.AdvancePastSeasonEnd();
            string newSeasonId = scenario.System.CurrentSeason.SeasonId;
            scenario.Service.CallLog.Clear();
            LeaderboardEntry entry = service.GetLocalEntryAsync(CancellationToken.None).Result;

            CollectionAssert.AreEqual(new[]
            {
                CountingLeagueGroupService.AddTrophiesCall + ":" + oldSeasonId,
                CountingLeagueGroupService.AddTrophiesCall + ":" + oldSeasonId,
                CountingLeagueGroupService.GetGroupCall + ":" + newSeasonId,
            }, scenario.Service.CallLog);
            Assert.AreEqual(0, entry.Score);
            Assert.AreEqual(0, scenario.System.PendingTrophyGrantCount);

            SeasonResult result = scenario.System.GetPendingSeasonResultAsync(CancellationToken.None).Result;
            Assert.AreEqual(oldSeasonId, result.SeasonId);
            Assert.AreEqual(queued, result.FinalTrophies);
        }

        [Test]
        public void TryGetScore_DoesNotCountUnsentTrophiesOfAnotherSeason()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            var service = new LeagueBoardService(scenario.System);
            _ = service.GetLocalEntryAsync(CancellationToken.None).Result;
            scenario.System.RecordLevelWin(NormalWin);
            scenario.AdvancePastSeasonEnd();

            // Gửi hỏng, bảng mùa mới vẫn tải được; cúp mùa cũ còn nằm trong hàng chờ.
            scenario.Simulation.FailNextCall();
            LeaderboardEntry entry = service.GetLocalEntryAsync(CancellationToken.None).Result;
            Assume.That(scenario.System.PendingTrophyGrantCount, Is.EqualTo(1));

            Assert.IsTrue(service.TryGetScore(out long score));
            Assert.AreEqual(entry.Score, score, "Cúp nợ của mùa cũ không phải điểm trên bảng mùa mới");
        }

        /// <summary>
        /// Mùa đổi giữa lúc diễn: <see cref="LeaderboardBoard.MarkRevealed"/> ở nhịp hạ cánh phải ghi snapshot theo mùa của bảng vừa
        /// diễn. Ghi theo mùa lúc hạ cánh thì lần mở sau so hạng mùa cũ với bảng mùa mới → RankUp/RankDown giả thay vì NEW.
        /// </summary>
        [Test]
        public void SeasonRollsDuringReveal_MarkRevealedKeepsLoadedSeason_NextOpenIsNewEntry()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            var service = new LeagueBoardService(scenario.System);
            var board = new LeaderboardBoard(new LeaderboardBoardSettings("league", new FetchWindowSettings(50, 4, 6, 15), new RankTierRule(3)),
                                             service, service, new InMemoryLeaderboardSnapshotStore());
            BoardScene revealing = board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result;
            string loadedSeasonId = scenario.System.CurrentSeason.SeasonId;
            Assert.AreEqual(loadedSeasonId, revealing.Change.SeasonKey);

            scenario.AdvancePastSeasonEnd();
            board.MarkRevealed(revealing.Change);
            BoardScene next = board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result;

            Assert.AreEqual(RankChangeKind.NewEntry, next.Change.Kind);
            Assert.AreNotEqual(loadedSeasonId, next.Change.SeasonKey);
        }

        /// <summary>
        /// Mốc đổi mùa rơi đúng lúc đang tải bảng (trong độ trễ của GetGroupAsync): entry của người chơi và khoá mùa đi kèm thay đổi
        /// phải cùng một mùa. Trước đây entry là mùa cũ còn khoá đọc sau khi tải là mùa mới → snapshot (hạng mùa cũ, khoá mùa mới) →
        /// lần mở sau diễn RankDown / RankUp giả thay vì đúng trạng thái.
        /// </summary>
        [Test]
        public void SeasonRollsWhileGroupIsLoading_ChangeKeyMatchesSeasonOfLocalEntry_NextOpenHasNoFakeRankChange()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            var service = new LeagueBoardService(scenario.System);
            var board = new LeaderboardBoard(new LeaderboardBoardSettings("league", new FetchWindowSettings(50, 4, 6, 15), new RankTierRule(3)),
                                             service, service, new InMemoryLeaderboardSnapshotStore());
            for (int index = 0; index < 12; index++) scenario.System.RecordLevelWin(NormalWin);
            BoardScene oldSeasonScene = board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result;
            board.MarkRevealed(oldSeasonScene.Change);
            string oldSeasonId = scenario.System.CurrentSeason.SeasonId;
            Assume.That(oldSeasonScene.Change.ToScore, Is.GreaterThan(0));

            // Thắng thêm (bảng cũ hết hiệu lực), rồi mở lại đúng lúc mùa sắp hết: mốc đổi mùa trôi qua khi GetGroupAsync đang chạy.
            int lastWinTrophies = scenario.System.RecordLevelWin(NormalWin).Trophies;
            bool crossed = false;
            scenario.Service.WhileGetGroupInFlight = () =>
            {
                if (crossed) return;
                crossed = true;
                scenario.AdvancePastSeasonEnd();
            };
            BoardScene boundaryScene = board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result;
            scenario.Service.WhileGetGroupInFlight = null;

            Assume.That(crossed, Is.True);
            string newSeasonId = scenario.System.CurrentSeason.SeasonId;
            Assert.AreNotEqual(oldSeasonId, newSeasonId);
            LeagueGroupSnapshot loaded = service.LastKnownSnapshot;
            Assert.AreEqual(newSeasonId, loaded.Season.SeasonId);
            Assert.AreEqual(loaded.Season.SeasonId, boundaryScene.Change.SeasonKey, "Khoá mùa phải là mùa của entry vừa tải");
            Assert.AreEqual(loaded.LocalRank, boundaryScene.Change.ToRank);
            Assert.AreEqual(loaded.LocalTrophies, boundaryScene.Change.ToScore);
            Assert.AreEqual(RankChangeKind.NewEntry, boundaryScene.Change.Kind);

            board.MarkRevealed(boundaryScene.Change);
            BoardScene nextScene = board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result;
            Assert.AreEqual(RankChangeKind.Unchanged, nextScene.Change.Kind,
                            "Snapshot (hạng mùa cũ, khoá mùa mới) sẽ làm lần mở này thành RankDown / RankUp giả");
            Assert.AreEqual(newSeasonId, nextScene.Change.SeasonKey);

            SeasonResult oldResult = scenario.System.GetPendingSeasonResultAsync(CancellationToken.None).Result;
            Assert.AreEqual(oldSeasonId, oldResult.SeasonId);
            Assert.AreEqual(oldSeasonScene.Change.ToScore + lastWinTrophies, oldResult.FinalTrophies, "Cúp thắng ngay trước mốc vẫn tính cho mùa cũ");
        }

        /// <summary>
        /// D: một nơi dùng khác của cùng service (HUD, widget thứ hai) đang tải bảng mùa cũ thì mốc đổi mùa trôi qua và trang mở ra.
        /// Lượt tải của mùa cũ không được dùng cho mùa mới (entry mùa cũ + khoá mùa mới → lần mở sau RankDown giả); khi nó xong muộn
        /// thì không ghi đè snapshot mùa mới và không xoá lượt đang bay của mùa mới.
        /// </summary>
        [Test]
        public void InFlightLoadOfOldSeason_IsNotReusedForNewSeason_AndItsLateCompletionDoesNotOverwriteNewerSnapshot()
        {
            WithoutSynchronizationContext.Run(() =>
            {
                LeagueScenario scenario = LeagueScenario.Create();
                var service = new LeagueBoardService(scenario.System);
                var board = new LeaderboardBoard(new LeaderboardBoardSettings("league", new FetchWindowSettings(50, 4, 6, 15), new RankTierRule(3)),
                                                 service, service, new InMemoryLeaderboardSnapshotStore());
                for (int index = 0; index < 12; index++) scenario.System.RecordLevelWin(NormalWin);
                SeasonWindow oldSeason = scenario.System.CurrentSeason;
                scenario.Clock.Set(oldSeason.EndUtc - TimeSpan.FromMinutes(1));
                BoardScene oldSeasonScene = board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result;
                board.MarkRevealed(oldSeasonScene.Change);
                Assume.That(oldSeasonScene.Change.ToScore, Is.GreaterThan(0));

                // Nơi dùng khác bắt đầu tải bảng mùa cũ; phản hồi về muộn.
                service.Invalidate();
                var oldSeasonResponse = new TaskCompletionSource<bool>();
                scenario.Service.GetGroupResponseGate = oldSeasonResponse;
                Task<IReadOnlyList<LeaderboardEntry>> otherUser = service.GetRangeAsync(0, 5, CancellationToken.None);

                // Qua mốc đổi mùa, trang mở ra trong lúc lượt mùa cũ còn bay.
                scenario.AdvancePastSeasonEnd();
                string newSeasonId = scenario.System.CurrentSeason.SeasonId;
                int groupCallsBeforePage = scenario.Service.GetGroupCallCount;
                var newSeasonResponse = new TaskCompletionSource<bool>();
                scenario.Service.GetGroupResponseGate = newSeasonResponse;
                Task<BoardScene> pageLoad = board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None);
                scenario.Service.GetGroupResponseGate = null;
                Assert.AreEqual(groupCallsBeforePage + 1, scenario.Service.GetGroupCallCount, "Mùa mới phải mở lượt tải riêng");

                newSeasonResponse.SetResult(true);
                BoardScene boundaryScene = pageLoad.Result;
                LeagueGroupSnapshot newSeasonSnapshot = service.LastKnownSnapshot;
                Assert.AreEqual(newSeasonId, newSeasonSnapshot.Season.SeasonId);
                Assert.AreEqual(newSeasonId, boundaryScene.Change.SeasonKey);
                Assert.AreEqual(newSeasonSnapshot.LocalRank, boundaryScene.Change.ToRank, "Entry phải thuộc mùa mới, không phải hạng mùa cũ");
                Assert.AreEqual(newSeasonSnapshot.LocalTrophies, boundaryScene.Change.ToScore);
                Assert.AreEqual(RankChangeKind.NewEntry, boundaryScene.Change.Kind);

                // Một lượt mùa mới nữa đang bay thì lượt mùa cũ mới về.
                service.Invalidate();
                var secondNewSeasonResponse = new TaskCompletionSource<bool>();
                scenario.Service.GetGroupResponseGate = secondNewSeasonResponse;
                Task<LeaderboardEntry> newSeasonInFlight = service.GetLocalEntryAsync(CancellationToken.None);
                scenario.Service.GetGroupResponseGate = null;
                oldSeasonResponse.SetResult(true);
                Assert.AreEqual(5, otherUser.Result.Count, "Người gọi của lượt mùa cũ vẫn nhận kết quả của mình");

                Assert.AreSame(newSeasonSnapshot, service.LastKnownSnapshot, "Lượt mùa cũ xong muộn không được ghi đè snapshot mùa mới");
                Assert.IsTrue(service.TryGetScore(out long score));
                Assert.AreEqual(newSeasonSnapshot.LocalTrophies, score);
                int groupCallsBeforeJoin = scenario.Service.GetGroupCallCount;
                Task<LeaderboardEntry> joined = service.GetLocalEntryAsync(CancellationToken.None);
                Assert.AreEqual(groupCallsBeforeJoin, scenario.Service.GetGroupCallCount, "Lượt đang bay của mùa mới vẫn phải được dùng lại");
                secondNewSeasonResponse.SetResult(true);
                Assert.AreEqual(newSeasonInFlight.Result.Rank, joined.Result.Rank);

                board.MarkRevealed(boundaryScene.Change);
                BoardScene nextScene = board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result;
                Assert.AreEqual(RankChangeKind.Unchanged, nextScene.Change.Kind, "Không có RankDown / RankUp giả ở lần mở sau");
                Assert.AreEqual(newSeasonId, nextScene.Change.SeasonKey);
            });
        }

        [Test]
        public void TryGetScore_SnapshotOfPreviousSeason_ReturnsFalse()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            var service = new LeagueBoardService(scenario.System);
            _ = service.GetLocalEntryAsync(CancellationToken.None).Result;
            Assume.That(service.TryGetScore(out _), Is.True);

            scenario.AdvancePastSeasonEnd();

            Assert.IsFalse(service.TryGetScore(out long score), "Điểm của snapshot mùa cũ không phải điểm của bảng mùa hiện tại");
            Assert.AreEqual(0, score);
        }

        /// <summary>
        /// Hai board dùng chung một service (HUD và popup): HUD mở lượt tải bằng token của nó, popup nhập cùng lượt, rồi HUD bị tắt. Chỉ
        /// HUD thôi chờ; lượt tải chạy tiếp cho popup. Trước đây lượt tải chạy bằng token của người mở nên popup nhận
        /// <see cref="OperationCanceledException"/> không phải của mình và widget hiện lỗi + Retry.
        /// </summary>
        [Test]
        public void SharedLoad_OpenerCancels_OtherBoardStillReceivesScene()
        {
            WithoutSynchronizationContext.Run(() =>
            {
                LeagueScenario scenario = LeagueScenario.Create();
                var service = new LeagueBoardService(scenario.System);
                LeaderboardBoard hudBoard = CreateBoard(service);
                LeaderboardBoard popupBoard = CreateBoard(service);
                scenario.System.RecordLevelWin(NormalWin);

                // HUD mở lượt tải (đang gửi cúp chờ), popup nhập cùng lượt.
                var responseGate = new TaskCompletionSource<bool>();
                scenario.Service.AddTrophiesResponseGate = responseGate;
                var hudLifetime = new CancellationTokenSource();
                Task<LeaderboardEntry> hud = hudBoard.SyncScoreAsync(hudLifetime.Token);
                Task<BoardScene> popup = popupBoard.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None);
                scenario.Service.AddTrophiesResponseGate = null;

                hudLifetime.Cancel();
                Assert.Throws<AggregateException>(() => hud.Wait(UnblockedCallTimeout));
                Assert.IsTrue(hud.IsCanceled, "Người huỷ thì nhận huỷ");
                Assert.IsFalse(popup.IsCompleted, "Người còn chờ không được nhận huỷ của người khác");

                responseGate.SetResult(true);
                BoardScene scene = popup.Result;

                Assert.AreEqual(1, scenario.Service.AddTrophiesCallCount);
                Assert.AreEqual(1, scenario.Service.GetGroupCallCount, "Popup dùng chung lượt tải, không mở lượt riêng");
                Assert.IsFalse(scenario.Service.LastGetGroupCancellationToken.IsCancellationRequested,
                               "Lượt gọi dịch vụ không được mang token đã huỷ của HUD");
                Assert.AreEqual(0, scenario.System.PendingTrophyGrantCount);
                Assert.GreaterOrEqual(scene.LocalRowIndex, 0);
                Assert.AreEqual(scenario.Simulation.LocalTrophies, scene.Rows[scene.LocalRowIndex].Entry.Score);
            });
        }

        /// <summary>
        /// Một board: lượt trình bày A mở lượt tải rồi bị huỷ (đóng popup), lượt trình bày B mở ngay sau. Backend không hỗ trợ huỷ nên lượt
        /// tải A còn treo tới khi phản hồi về. Lượt đã bị mọi người chờ bỏ (đang huỷ) không nhận thêm người: B mở lượt mới và nhận
        /// BoardScene mà không phải chờ phản hồi của A; phản hồi của A về muộn không ghi đè snapshot của lượt mới hơn.
        /// </summary>
        [Test]
        public void SameBoard_LoadCancelledThenReopenedImmediately_ReceivesSceneFromNewLoad()
        {
            WithoutSynchronizationContext.Run(() =>
            {
                LeagueScenario scenario = LeagueScenario.Create();
                var service = new LeagueBoardService(scenario.System);
                LeaderboardBoard board = CreateBoard(service);

                var abandonedResponse = new TaskCompletionSource<bool>();
                scenario.Service.GetGroupResponseGate = abandonedResponse;
                scenario.Service.ResponseGateIgnoresCancellation = true;
                var presentationA = new CancellationTokenSource();
                Task<BoardScene> loadA = board.LoadSceneAsync(BoardPresentMode.RevealIfPending, presentationA.Token);
                scenario.Service.GetGroupResponseGate = null;
                scenario.Service.ResponseGateIgnoresCancellation = false;

                presentationA.Cancel();
                Assert.Throws<AggregateException>(() => loadA.Wait(UnblockedCallTimeout), "A huỷ thì A kết thúc ngay, không chờ backend");
                Assert.IsTrue(loadA.IsCanceled);

                var presentationB = new CancellationTokenSource();
                Task<BoardScene> loadB = board.LoadSceneAsync(BoardPresentMode.RevealIfPending, presentationB.Token);
                Assert.IsTrue(loadB.Wait(UnblockedCallTimeout), "B không được nhập vào lượt tải A đã bỏ (phản hồi của nó còn treo)");
                BoardScene scene = loadB.Result;

                Assert.AreEqual(2, scenario.Service.GetGroupCallCount, "B mở lượt tải mới");
                Assert.AreEqual(RankChangeKind.NewEntry, scene.Change.Kind);
                Assert.GreaterOrEqual(scene.LocalRowIndex, 0);
                LeagueGroupSnapshot loadedForB = service.LastKnownSnapshot;
                Assert.IsNotNull(loadedForB);

                abandonedResponse.SetResult(true);
                Assert.AreSame(loadedForB, service.LastKnownSnapshot, "Phản hồi muộn của lượt A không ghi đè snapshot của lượt B");
            });
        }

        /// <summary>
        /// Mọi người chờ đều huỷ: không còn ai cần bảng nên lượt tải dừng — lượt gọi dịch vụ đang bay bị huỷ, không ghi snapshot. Lần hỏi
        /// sau mở lượt mới (không nhập vào lượt đã huỷ) và chạy bình thường.
        /// </summary>
        [Test]
        public void SharedLoad_EveryWaiterCancels_CancelsServiceCall_AndNextQueryLoadsNormally()
        {
            WithoutSynchronizationContext.Run(() =>
            {
                LeagueScenario scenario = LeagueScenario.Create();
                var service = new LeagueBoardService(scenario.System);

                var responseGate = new TaskCompletionSource<bool>();
                scenario.Service.GetGroupResponseGate = responseGate;
                var firstCaller = new CancellationTokenSource();
                var secondCaller = new CancellationTokenSource();
                Task<LeaderboardEntry> first = service.GetLocalEntryAsync(firstCaller.Token);
                Task<IReadOnlyList<LeaderboardEntry>> second = service.GetRangeAsync(0, 10, secondCaller.Token);
                scenario.Service.GetGroupResponseGate = null;
                CancellationToken serviceCallToken = scenario.Service.LastGetGroupCancellationToken;
                Assert.AreEqual(1, scenario.Service.GetGroupCallCount, "Hai người hỏi dùng chung một lượt tải");

                firstCaller.Cancel();
                Assert.Throws<AggregateException>(() => first.Wait(UnblockedCallTimeout));
                Assert.IsTrue(first.IsCanceled);
                Assert.IsFalse(second.IsCompleted, "Một người huỷ không dừng lượt tải của người còn chờ");
                Assert.IsFalse(serviceCallToken.IsCancellationRequested, "Còn người chờ thì lượt gọi dịch vụ chưa bị huỷ");

                secondCaller.Cancel();
                Assert.Throws<AggregateException>(() => second.Wait(UnblockedCallTimeout));
                Assert.IsTrue(second.IsCanceled);
                Assert.IsTrue(serviceCallToken.IsCancellationRequested, "Không còn ai chờ: lượt gọi dịch vụ phải bị huỷ");
                Assert.IsNull(service.LastKnownSnapshot, "Lượt bị huỷ không ghi snapshot");

                LeaderboardEntry entry = service.GetLocalEntryAsync(CancellationToken.None).Result;

                Assert.IsNotNull(entry);
                Assert.AreEqual(2, scenario.Service.GetGroupCallCount, "Lần hỏi sau mở lượt mới");
                Assert.IsFalse(scenario.Service.LastGetGroupCancellationToken.IsCancellationRequested);
                Assert.IsNotNull(service.LastKnownSnapshot);
                Assert.AreEqual(10, service.GetRangeAsync(0, 10, CancellationToken.None).Result.Count, "Bảng vừa tải dùng lại được");
                Assert.AreEqual(2, scenario.Service.GetGroupCallCount);
            });
        }

        /// <summary>
        /// League chỉ chạy trên main thread: nơi lưu kiểu PlayerPrefs, đồng hồ của host và UI nghe StateChanged / ScoreChanged đều đòi
        /// main thread. Backend thật hay hoàn tất Task trên thread pool; phần tiếp theo sau mỗi await (gửi cúp, gán snapshot, đọc đồng
        /// hồ, lưu mốc đồng hồ) phải quay về context của nơi gọi. Trước đây <see cref="LeagueBoardService"/> dùng ConfigureAwait(false)
        /// nên phần sau GetGroupAsync chạy trên thread pool: đọc đồng hồ và <see cref="MonotonicLeagueClock"/> ghi mốc xuống nơi lưu
        /// ngoài main thread. Chạy đủ các đường: mở trang khi còn cúp chờ gửi, hết mùa còn cúp mùa cũ, gửi điểm, HUD huỷ từ thread nền
        /// (như timer của CancelAfter) khi đang dùng chung lượt tải với trang.
        /// </summary>
        [Test]
        public void BackendCompletingOnThreadPool_StoreClockServiceCallsAndEvents_StayOnMainThread()
        {
            var recorder = new ThreadAffinityRecorder();
            var store = new MainThreadOnlyLeagueTextStore(recorder);
            ManualLeagueClock manualClock = LeagueTestFactory.CreateClockInFirstSeason();
            var clock = new MainThreadOnlyLeagueClock(new MonotonicLeagueClock(manualClock, store, ClockHighWaterKey), recorder);
            FixedLengthSeasonSchedule schedule = LeagueTestFactory.CreateSchedule();
            LeagueRules rules = LeagueTestFactory.CreateRules();
            var simulation = new SimulatedLeagueGroupService(LeagueTestFactory.CreateSimulationOptions(), rules, clock, store);
            var backend = new ThreadPoolCompletingLeagueGroupService(simulation, recorder)
            {
                // Mỗi phản hồi về sau một khoảng mạng đủ dài để đồng hồ không-lùi phải lưu mốc ở lần đọc kế tiếp.
                BeforeEachResponse = () => manualClock.Advance(MonotonicLeagueClock.HighWaterPersistStep),
            };
            LeagueSystem system = new LeagueSystemBuilder(LeagueScenario.SystemId, rules, LeagueTestFactory.CreateStreakLadder())
                                  .WithGroupService(backend)
                                  .WithSchedule(schedule)
                                  .WithClock(clock)
                                  .WithTextStore(store)
                                  .WithRewardGranter(new RecordingLeagueRewardGranter())
                                  .WithFeatureGate(new ManualLeagueFeatureGate(true))
                                  .WithTrophyRule(new MultipliedTrophyRule(new[] { 10, 15, 20 }))
                                  .Build();
            int stateChangedCount = 0;
            system.StateChanged += () =>
            {
                recorder.Check("StateChanged");
                stateChangedCount++;
            };
            var service = new LeagueBoardService(system);
            service.ScoreChanged += () => recorder.Check("ScoreChanged");
            LeaderboardBoard pageBoard = CreateBoard(service);
            LeaderboardBoard hudBoard = CreateBoard(service);

            using (SingleThreadSynchronizationContext mainThread = SingleThreadSynchronizationContext.Install())
            {
                Func<bool> releaseResponse = backend.TryReleaseNextResponseOnThreadPool;

                // Mở trang lần đầu khi còn hai trận thắng chưa gửi: gửi cúp rồi tải bảng.
                system.RecordLevelWin(NormalWin);
                system.RecordLevelWin(NormalWin);
                Task<BoardScene> firstOpen = pageBoard.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None);
                mainThread.PumpUntilCompleted(firstOpen, releaseResponse);
                pageBoard.MarkRevealed(firstOpen.Result.Change);

                // Thắng rồi hết mùa: mở trang gửi cúp mùa cũ trước, rồi tải bảng mùa mới.
                system.RecordLevelWin(NormalWin);
                manualClock.Set(schedule.GetSeasonAt(clock.UtcNow).EndUtc + TimeSpan.FromHours(1));
                Task<BoardScene> seasonRollOpen = pageBoard.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None);
                mainThread.PumpUntilCompleted(seasonRollOpen, releaseResponse);
                Assert.AreEqual(system.CurrentSeason.SeasonId, seasonRollOpen.Result.Change.SeasonKey);

                // Thắng rồi gửi điểm trực tiếp (đường SyncScoreAsync khi điểm lệch).
                system.RecordLevelWin(NormalWin);
                Task<LeaderboardEntry> submitted = service.SubmitScoreAsync(0, CancellationToken.None);
                mainThread.PumpUntilCompleted(submitted, releaseResponse);
                Assert.IsNotNull(submitted.Result);
                CollectionAssert.IsEmpty(recorder.Violations, "Việc chỉ được làm trên main thread đã chạy trên thread pool");

                // HUD mở lượt tải, trang nhập cùng lượt, token của HUD bị huỷ từ thread nền.
                system.RecordLevelWin(NormalWin);
                var hudLifetime = new CancellationTokenSource();
                Task<LeaderboardEntry> hud = hudBoard.SyncScoreAsync(hudLifetime.Token);
                Task<BoardScene> page = pageBoard.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None);
                ThreadPoolWork.RunAndWait(hudLifetime.Cancel);
                mainThread.PumpUntilCompleted(hud, releaseResponse);
                mainThread.PumpUntilCompleted(page, releaseResponse);

                Assert.IsTrue(hud.IsCanceled);
                Assert.IsNotNull(page.Result);
            }

            Assert.AreEqual(0, system.PendingTrophyGrantCount);
            Assert.Greater(backend.ResponsesReleasedOnThreadPool, 0, "Test phải thật sự cho phản hồi tới trên thread pool");
            Assert.Greater(stateChangedCount, 0);
            CollectionAssert.IsEmpty(recorder.Violations, "Việc chỉ được làm trên main thread đã chạy trên thread pool");
        }

        private static LeaderboardBoard CreateBoard(LeagueBoardService service)
        {
            return new LeaderboardBoard(new LeaderboardBoardSettings("league", new FetchWindowSettings(50, 4, 6, 15), new RankTierRule(3)),
                                        service, service, new InMemoryLeaderboardSnapshotStore());
        }
    }
}
