using System.Collections.Generic;
using NUnit.Framework;

namespace DreamTech.Leaderboard.Tests
{
    [TestFixture]
    public class BoardRowsBuilderTests
    {
        private static LeaderboardEntry Entry(string id, int rank, long score = 0)
        {
            return new LeaderboardEntry(id, id, score == 0 ? 1000 - rank : score, rank);
        }

        [Test]
        public void Build_DuplicateIds_LocalEntryWins()
        {
            var top = new List<LeaderboardEntry> { Entry("a", 0), Entry("me", 1, 500) };
            LeaderboardEntry local = Entry("me", 1, 900);

            List<BoardRow> rows = BoardRowsBuilder.Build(top, null, local, "me");

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual(900, rows[1].Entry.Score);
            Assert.IsTrue(rows[1].IsLocalPlayer);
        }

        [Test]
        public void Build_RankJump_InsertsSingleGapRow()
        {
            var top = new List<LeaderboardEntry> { Entry("a", 0), Entry("b", 1) };
            var window = new List<LeaderboardEntry> { Entry("c", 10), Entry("d", 11) };

            List<BoardRow> rows = BoardRowsBuilder.Build(top, window, null, "me");

            Assert.AreEqual(5, rows.Count);
            Assert.IsTrue(rows[2].IsGap);
            Assert.AreEqual(10, rows[3].Entry.Rank);
        }

        [Test]
        public void Build_UnsortedInput_SortsByRankThenScoreThenId()
        {
            var window = new List<LeaderboardEntry> { Entry("z", 2, 100), Entry("b", 1, 50), Entry("a", 1, 50), Entry("c", 0) };

            List<BoardRow> rows = BoardRowsBuilder.Build(null, window, null, null);

            CollectionAssert.AreEqual(new[] { "c", "a", "b", "z" }, new[] { rows[0].Entry.PlayerId, rows[1].Entry.PlayerId, rows[2].Entry.PlayerId, rows[3].Entry.PlayerId });
        }

        [Test]
        public void IndexOfLocal_NoLocal_ReturnsMinusOne()
        {
            List<BoardRow> rows = BoardRowsBuilder.Build(new List<LeaderboardEntry> { Entry("a", 0) }, null, null, "me");
            Assert.AreEqual(-1, BoardRowsBuilder.IndexOfLocal(rows));
        }
    }

    [TestFixture]
    public class FetchWindowPlannerTests
    {
        private static readonly FetchWindowSettings Settings = new FetchWindowSettings(50, 4, 6, 15);

        [Test]
        public void Plan_SmallClimbOutsideTop_WindowCoversAnimatedRowsAndBelow()
        {
            RankChange change = RankChange.Create(RankChangeKind.RankUp, 400, 388, 0, 0);

            FetchPlan plan = FetchWindowPlanner.Plan(change, Settings);

            Assert.AreEqual(12, plan.AnimatedPasses);
            Assert.IsFalse(plan.IsCompressed);
            Assert.AreEqual(384, plan.Window.Offset);
            Assert.AreEqual(388 + 12 + 6 - 384 + 1, plan.Window.Count);
        }

        [Test]
        public void Plan_BigJump_CompressedWindowStopsAtAnimatedRows()
        {
            RankChange change = RankChange.Create(RankChangeKind.RankUp, 900, 120, 0, 0);

            FetchPlan plan = FetchWindowPlanner.Plan(change, Settings);

            Assert.AreEqual(15, plan.AnimatedPasses);
            Assert.IsTrue(plan.IsCompressed);
            Assert.AreEqual(116, plan.Window.Offset);
            Assert.AreEqual(120 + 15 - 116 + 1, plan.Window.Count);
        }

        [Test]
        public void Plan_WindowInsideTop_WindowIsEmpty()
        {
            RankChange change = RankChange.Create(RankChangeKind.RankUp, 10, 5, 0, 0);

            FetchPlan plan = FetchWindowPlanner.Plan(change, Settings);

            Assert.IsTrue(plan.Window.IsEmpty);
            Assert.AreEqual(50, plan.Top.Count);
        }

        [Test]
        public void Plan_NoLocalEntry_TopOnly()
        {
            FetchPlan plan = FetchWindowPlanner.Plan(RankChange.NoLocalEntry, Settings);

            Assert.IsTrue(plan.Window.IsEmpty);
            Assert.AreEqual(0, plan.AnimatedPasses);
        }
    }

    [TestFixture]
    public class RankChangeTests
    {
        private static readonly LeaderboardEntry Current = new LeaderboardEntry("me", "Me", 500, 10);

        [Test]
        public void Resolve_NoSnapshot_IsNewEntry()
        {
            RankChange change = RankChange.Resolve(null, Current, "s1");
            Assert.AreEqual(RankChangeKind.NewEntry, change.Kind);
            Assert.AreEqual(10, change.ToRank);
        }

        [Test]
        public void Resolve_DifferentSeason_IsNewEntry()
        {
            RankChange change = RankChange.Resolve(new RevealSnapshot(20, 100, "s0"), Current, "s1");
            Assert.AreEqual(RankChangeKind.NewEntry, change.Kind);
        }

        [Test]
        public void Resolve_BetterRank_IsRankUpWithPassedCount()
        {
            RankChange change = RankChange.Resolve(new RevealSnapshot(22, 100, "s1"), Current, "s1");
            Assert.AreEqual(RankChangeKind.RankUp, change.Kind);
            Assert.AreEqual(12, change.PassedCount);
            Assert.AreEqual(100, change.FromScore);
            Assert.AreEqual(500, change.ToScore);
        }

        [Test]
        public void Resolve_WorseRank_IsRankDown()
        {
            RankChange change = RankChange.Resolve(new RevealSnapshot(5, 500, "s1"), Current, "s1");
            Assert.AreEqual(RankChangeKind.RankDown, change.Kind);
            Assert.AreEqual(0, change.PassedCount);
        }

        [Test]
        public void Resolve_SameRankHigherScore_IsScoreImproved()
        {
            RankChange change = RankChange.Resolve(new RevealSnapshot(10, 400, "s1"), Current, "s1");
            Assert.AreEqual(RankChangeKind.ScoreImproved, change.Kind);
        }

        [Test]
        public void Resolve_Identical_IsUnchanged()
        {
            RankChange change = RankChange.Resolve(new RevealSnapshot(10, 500, "s1"), Current, "s1");
            Assert.AreEqual(RankChangeKind.Unchanged, change.Kind);
        }

        [Test]
        public void Resolve_NullCurrent_IsNoLocalEntry()
        {
            RankChange change = RankChange.Resolve(new RevealSnapshot(10, 500, "s1"), null, "s1");
            Assert.AreEqual(RankChangeKind.NoLocalEntry, change.Kind);
            Assert.IsFalse(change.HasLocalEntry);
        }
    }

    [TestFixture]
    public class RankTierRuleTests
    {
        [Test]
        public void Classify_DefaultRule_SplitsFirstPodiumStandard()
        {
            RankTierRule rule = RankTierRule.Default;
            Assert.AreEqual(RankTier.FirstPlace, rule.Classify(0));
            Assert.AreEqual(RankTier.Podium, rule.Classify(1));
            Assert.AreEqual(RankTier.Podium, rule.Classify(2));
            Assert.AreEqual(RankTier.Standard, rule.Classify(3));
            Assert.AreEqual(RankTier.Standard, rule.Classify(-1));
        }

        [Test]
        public void DefaultStruct_BehavesLikeDefaultRule()
        {
            RankTierRule rule = default;
            Assert.AreEqual(RankTier.Podium, rule.Classify(2));
            Assert.AreEqual(2, rule.MedalIndex(2));
            Assert.AreEqual(-1, rule.MedalIndex(3));
        }
    }

    [TestFixture]
    public class LeaderboardBoardRegistryTests
    {
        [TearDown]
        public void TearDown()
        {
            LeaderboardBoardRegistry.Reset();
        }

        private static LeaderboardBoard CreateBoard(string boardId)
        {
            return new LeaderboardBoard(new LeaderboardBoardSettings(boardId, FetchWindowSettings.Default, RankTierRule.Default),
                                        new FakeLeaderboardService(), null, new InMemoryLeaderboardSnapshotStore());
        }

        [Test]
        public void Register_ThenTryGet_ReturnsBoard()
        {
            LeaderboardBoard board = CreateBoard("main");
            LeaderboardBoardRegistry.Register(board);

            Assert.IsTrue(LeaderboardBoardRegistry.TryGet("main", out ILeaderboardBoard found));
            Assert.AreSame(board, found);
        }

        [Test]
        public void Unregister_DifferentInstanceSameId_KeepsRegisteredBoard()
        {
            LeaderboardBoard registered = CreateBoard("main");
            LeaderboardBoardRegistry.Register(registered);

            Assert.IsFalse(LeaderboardBoardRegistry.Unregister(CreateBoard("main")));
            Assert.IsTrue(LeaderboardBoardRegistry.TryGet("main", out _));
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            LeaderboardBoardRegistry.Register(CreateBoard("main"));
            LeaderboardBoardRegistry.Reset();
            Assert.IsFalse(LeaderboardBoardRegistry.TryGet("main", out _));
        }
    }
}
