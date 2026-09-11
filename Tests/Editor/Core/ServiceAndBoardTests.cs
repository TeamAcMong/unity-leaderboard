using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DreamTech.Leaderboard.Tests
{
    [TestFixture]
    public class MockLeaderboardServiceTests
    {
        private static MockLeaderboardService Create(int botCount = 100, long minimumScore = 300, long maximumScore = 250000, int seed = 1)
        {
            return new MockLeaderboardService(new MockLeaderboardOptions
            {
                BotCount = botCount,
                Seed = seed,
                MinScore = minimumScore,
                MaxScore = maximumScore,
                LatencyMilliseconds = 0,
            });
        }

        [Test]
        public void SubmitScore_LowerThanBest_KeepsBest()
        {
            MockLeaderboardService service = Create();
            service.SetLocalScore(service.ScoreToReachRank(10));
            long best = service.LocalScore;

            LeaderboardEntry entry = service.SubmitScoreAsync(5, CancellationToken.None).Result;

            Assert.AreEqual(best, entry.Score);
        }

        [Test]
        public void Ties_SameSeed_ProduceIdenticalOrderAndLocalAfterEqualBots()
        {
            MockLeaderboardService first = Create(botCount: 300, minimumScore: 1, maximumScore: 10);
            MockLeaderboardService second = Create(botCount: 300, minimumScore: 1, maximumScore: 10);

            IReadOnlyList<LeaderboardEntry> firstRange = first.GetRangeAsync(0, 300, CancellationToken.None).Result;
            IReadOnlyList<LeaderboardEntry> secondRange = second.GetRangeAsync(0, 300, CancellationToken.None).Result;
            CollectionAssert.AreEqual(firstRange.Select(entry => entry.PlayerId), secondRange.Select(entry => entry.PlayerId));

            first.SetLocalScore(5);
            IReadOnlyList<LeaderboardEntry> all = first.GetRangeAsync(0, 301, CancellationToken.None).Result;
            int localRank = first.LocalRank;
            Assert.AreEqual(first.LocalPlayerId, all[localRank].PlayerId);
            Assert.IsTrue(all.Take(localRank).All(entry => entry.Score >= 5), "Mọi người đứng trên phải có điểm >= người chơi");
            Assert.IsTrue(all.Skip(localRank + 1).All(entry => entry.Score < 5), "Hoà điểm thì người chơi đứng sau bot");
        }

        [Test]
        public void GetRange_OutOfBounds_IsClamped()
        {
            MockLeaderboardService service = Create(botCount: 10);
            Assert.AreEqual(5, service.GetRangeAsync(5, 100, CancellationToken.None).Result.Count);
            Assert.AreEqual(3, service.GetRangeAsync(-2, 3, CancellationToken.None).Result.Count);
            Assert.AreEqual(0, service.GetRangeAsync(50, 5, CancellationToken.None).Result.Count);
        }

        [Test]
        public void FailNextCall_ThrowsExactlyOnce()
        {
            MockLeaderboardService service = Create();
            service.FailNextCall();

            AggregateException exception = Assert.Throws<AggregateException>(() => _ = service.GetLocalEntryAsync(CancellationToken.None).Result);
            Assert.IsInstanceOf<MockLeaderboardException>(exception.InnerException);
            Assert.DoesNotThrow(() => _ = service.GetLocalEntryAsync(CancellationToken.None).Result);
        }

        [Test]
        public void FeaturedNames_AreAssignedToBots()
        {
            var service = new MockLeaderboardService(new MockLeaderboardOptions
            {
                BotCount = 30,
                LatencyMilliseconds = 0,
                FeaturedNames = new[] { "Nguyễn Thị Ánh" },
                FeaturedNameEvery = 3,
            });

            IReadOnlyList<LeaderboardEntry> all = service.GetRangeAsync(0, 30, CancellationToken.None).Result;
            Assert.AreEqual(10, all.Count(entry => entry.DisplayName == "Nguyễn Thị Ánh"));
        }
    }

    [TestFixture]
    public class LeaderboardBoardTests
    {
        private FakeLeaderboardService _service;
        private ManualScoreSource _scoreSource;
        private InMemoryLeaderboardSnapshotStore _snapshotStore;
        private LeaderboardBoard _board;

        [SetUp]
        public void SetUp()
        {
            _service = new FakeLeaderboardService();
            for (int index = 0; index < 20; index++) _service.SetPlayer("bot-" + index.ToString("00"), 1000 - index * 10);
            _scoreSource = new ManualScoreSource("test");
            _snapshotStore = new InMemoryLeaderboardSnapshotStore();
            _board = new LeaderboardBoard(new LeaderboardBoardSettings("main", new FetchWindowSettings(5, 2, 2, 10), RankTierRule.Default),
                                          _service, _scoreSource, _snapshotStore);
        }

        [Test]
        public void SyncScore_SourceHigherThanBackend_Submits()
        {
            _service.SetPlayer("me", 100);
            _scoreSource.SetScore(955);

            LeaderboardEntry entry = _board.SyncScoreAsync(CancellationToken.None).Result;

            Assert.AreEqual(1, _service.SubmitCallCount);
            Assert.AreEqual(955, entry.Score);
            Assert.AreEqual(5, entry.Rank);
        }

        [Test]
        public void SyncScore_SourceNotHigher_DoesNotSubmit()
        {
            _service.SetPlayer("me", 500);
            _scoreSource.SetScore(500);

            _board.SyncScoreAsync(CancellationToken.None).Wait();

            Assert.AreEqual(0, _service.SubmitCallCount);
        }

        [Test]
        public void SyncScore_SourceWithoutScore_NeverSubmits()
        {
            _board.SyncScoreAsync(CancellationToken.None).Wait();
            Assert.AreEqual(0, _service.SubmitCallCount);
        }

        [Test]
        public void SyncScore_SubmitFails_NextSyncRetries()
        {
            _scoreSource.SetScore(700);
            _service.FailNextSubmit = true;

            Assert.Throws<AggregateException>(() => _board.SyncScoreAsync(CancellationToken.None).Wait());
            LeaderboardEntry entry = _board.SyncScoreAsync(CancellationToken.None).Result;

            Assert.AreEqual(2, _service.SubmitCallCount);
            Assert.AreEqual(700, entry.Score);
        }

        [Test]
        public void LoadScene_NoSnapshot_IsNewEntry()
        {
            _scoreSource.SetScore(875);

            BoardScene scene = _board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result;

            Assert.AreEqual(RankChangeKind.NewEntry, scene.Change.Kind);
            Assert.GreaterOrEqual(scene.LocalRowIndex, 0);
            Assert.IsTrue(scene.Rows[scene.LocalRowIndex].IsLocalPlayer);
        }

        [Test]
        public void LoadScene_AfterMarkRevealed_IsUnchanged()
        {
            _scoreSource.SetScore(875);
            BoardScene first = _board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result;
            _board.MarkRevealed(first.Change);

            BoardScene second = _board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result;

            Assert.AreEqual(RankChangeKind.Unchanged, second.Change.Kind);
            Assert.IsFalse(_board.HasUnrevealedChange);
        }

        [Test]
        public void LoadScene_RankImproved_IsRankUpWithPassedCount()
        {
            _scoreSource.SetScore(805);
            _board.MarkRevealed(_board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result.Change);
            int previousRank = _board.LastKnownLocalEntry.Rank;

            _scoreSource.SetScore(955);
            Assert.IsTrue(_board.HasUnrevealedChange);
            BoardScene scene = _board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result;

            Assert.AreEqual(RankChangeKind.RankUp, scene.Change.Kind);
            Assert.AreEqual(previousRank - scene.Change.ToRank, scene.Change.PassedCount);
        }

        [Test]
        public void LoadScene_Browse_IsAlwaysUnchanged()
        {
            _scoreSource.SetScore(955);
            BoardScene scene = _board.LoadSceneAsync(BoardPresentMode.Browse, CancellationToken.None).Result;
            Assert.AreEqual(RankChangeKind.Unchanged, scene.Change.Kind);
        }

        [Test]
        public void LoadScene_SeasonChanged_IsNewEntryAgain()
        {
            _scoreSource.SetScore(875);
            _board.MarkRevealed(_board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result.Change);

            _service.SeasonKey = "season-2";
            BoardScene scene = _board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result;

            Assert.AreEqual(RankChangeKind.NewEntry, scene.Change.Kind);
        }

        [Test]
        public void LoadScene_NoLocalEntry_ShowsTopOnly()
        {
            BoardScene scene = _board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result;

            Assert.AreEqual(RankChangeKind.NoLocalEntry, scene.Change.Kind);
            Assert.AreEqual(-1, scene.LocalRowIndex);
            Assert.AreEqual(5, scene.Rows.Count);
            Assert.IsFalse(scene.NeedsReveal);
        }

        [Test]
        public void LoadScene_Cancelled_Throws()
        {
            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            AggregateException exception = Assert.Throws<AggregateException>(() =>
                _board.LoadSceneAsync(BoardPresentMode.Browse, cancellation.Token).Wait());
            Assert.IsInstanceOf<OperationCanceledException>(exception.InnerException);
        }

        [Test]
        public void TryBeginReveal_SecondWhileActive_Fails_UntilLeaseDisposed()
        {
            Assert.IsTrue(_board.TryBeginReveal(out IDisposable lease));
            Assert.IsFalse(_board.TryBeginReveal(out _));

            lease.Dispose();

            Assert.IsTrue(_board.TryBeginReveal(out IDisposable secondLease));
            secondLease.Dispose();
        }

        [Test]
        public void HasUnrevealedChange_BeforeAnySync_FollowsScoreSource()
        {
            Assert.IsFalse(_board.HasUnrevealedChange);
            _scoreSource.SetScore(10);
            Assert.IsTrue(_board.HasUnrevealedChange);
        }
    }
}
