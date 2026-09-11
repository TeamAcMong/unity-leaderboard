using System;
using System.Collections.Generic;
using System.Linq;
using DreamTech.Leaderboard.ViewModel;
using NUnit.Framework;

namespace DreamTech.Leaderboard.Tests
{
    /// <summary>
    /// Port của Tests~/Sim.cs (bản tham khảo), nhưng chạy trên RevealTimeline THẬT thay vì chép lại công thức:
    /// 8 kịch bản và toàn bộ bất biến giữ nguyên.
    /// </summary>
    [TestFixture]
    public class RankUpPlannerInvariantTests
    {
        private const float FineDeltaTime = 0.0005f;
        private const int MaximumTicks = 20000;

        private static IEnumerable<TestCaseData> Scenarios()
        {
            yield return new TestCaseData(30, 27).SetName("SmallClimbInTopList");
            yield return new TestCaseData(400, 388).SetName("ClimbOutsideTopWithGap");
            yield return new TestCaseData(900, 120).SetName("BigJumpCompressed");
            yield return new TestCaseData(300, 5).SetName("JumpIntoTopListCompressed");
            yield return new TestCaseData(10, 0).SetName("ReachFirstPlace");
            yield return new TestCaseData(200, 182).SetName("ExactlyMaximumAnimated");
            yield return new TestCaseData(150, 150).SetName("NoChange");
            yield return new TestCaseData(-1, 75).SetName("NewPlayer");
        }

        [TestCaseSource(nameof(Scenarios))]
        public void Scenario_KeepsAllInvariants(int startRank, int targetRank)
        {
            LeaderboardScenario scenario = LeaderboardScenario.Create(maximumAnimatedPasses: 18);
            BoardScene scene = scenario.LoadRevealScene(startRank, targetRank);

            // Dữ liệu sau khi dựng: slot == index, hạng tăng dần, có dòng "..." ở chỗ đứt quãng, row mình đúng hạng mới.
            var model = new BoardModel(scene, new MotionSettings());
            AssertRowsSettled(model, "after-build");
            Assert.AreEqual(scene.Change.ToRank, model.LocalRow.Entry.Rank);
            for (int index = 1; index < model.Rows.Count; index++)
            {
                RowState current = model.Rows[index];
                RowState previous = model.Rows[index - 1];
                if (!current.IsGap && !previous.IsGap) Assert.AreEqual(previous.Entry.Rank + 1, current.Entry.Rank, "Thiếu dòng '...' giữa hai đoạn hạng");
            }

            if (scene.Change.Kind != RankChangeKind.RankUp)
            {
                Assert.AreEqual(targetRank == startRank ? RankChangeKind.Unchanged : RankChangeKind.NewEntry, scene.Change.Kind);
                return;
            }

            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Prepare();
            RankUpPlan plan = timeline.Plan;
            int expectedAnimated = Math.Min(scene.Change.PassedCount, 18);
            Assert.AreEqual(expectedAnimated, plan.AnimatedCount, "count == animated");

            AssertDisplayOrder(model, row => row.Slot, "before-state");
            if (plan.IsCompressed)
            {
                int oldRank = model.LocalRow.DisplayRank;
                model.LocalRow.SetDisplayRankImmediate(plan.SpinToRank);
                AssertDisplayOrder(model, row => row.Slot, "after-spin");
                model.LocalRow.SetDisplayRankImmediate(oldRank);
            }

            timeline.Start();
            float maximumOverlap = 0f;
            int lastCrossed = 0;
            for (int tick = 0; tick < MaximumTicks && !timeline.IsFinished; tick++)
            {
                timeline.Tick(FineDeltaTime);
                model.Advance(FineDeltaTime);

                if (timeline.CrossedCount != lastCrossed)
                {
                    lastCrossed = timeline.CrossedCount;
                    RowState local = model.LocalRow;
                    AssertDisplayOrder(model, row => row == local ? (float)Math.Round(row.Slot) - 0.1f : row.TargetSlot, "crossing " + lastCrossed);
                }
                if (timeline.Phase == RevealPhase.Climb && timeline.CrossedCount < plan.AnimatedCount)
                {
                    RowState nextRow = plan.Passed[timeline.CrossedCount];
                    maximumOverlap = Math.Max(maximumOverlap, 1f - (model.LocalRow.Slot - nextRow.Slot));
                }
            }

            Assert.IsTrue(timeline.IsFinished, "Timeline phải kết thúc");
            Assert.LessOrEqual(maximumOverlap, new MotionSettings().MakeRoomAt + 0.02f, "Người phía trên phải nhường chỗ trước khi bị chồng lấn nhiều");
            Assert.AreEqual(plan.AnimatedCount, timeline.CrossedCount, "all crossed");
            model.FinishAllTweens();
            Assert.AreEqual(plan.LocalIndex, model.LocalRow.Slot, 1e-4f, "row mình hạ đúng slot cuối");
            Assert.AreEqual(scene.Change.ToRank, model.LocalRow.DisplayRank);
            AssertRowsSettled(model, "final");
        }

        internal static void AssertRowsSettled(BoardModel model, string tag)
        {
            for (int index = 0; index < model.Rows.Count; index++)
            {
                RowState row = model.Rows[index];
                Assert.AreEqual(index, row.Slot, 1e-4f, tag + ": slot " + index);
                if (!row.IsGap) Assert.AreEqual(row.Entry.Rank, row.DisplayRank, tag + ": hạng hiển thị của row " + index);
            }
        }

        private static void AssertDisplayOrder(BoardModel model, Func<RowState, float> slotOf, string tag)
        {
            List<RowState> ordered = model.Rows.Where(row => !row.IsGap).OrderBy(slotOf).ToList();
            for (int index = 1; index < ordered.Count; index++)
            {
                Assert.Greater(ordered[index].DisplayRank, ordered[index - 1].DisplayRank,
                               tag + ": thứ tự hiển thị " + ordered[index - 1].DisplayRank + " !< " + ordered[index].DisplayRank);
            }
            List<float> slots = model.Rows.Select(slotOf).ToList();
            Assert.AreEqual(slots.Count, slots.Distinct().Count(), tag + ": hai row chung một slot");
        }
    }

    [TestFixture]
    public class RevealTimelineTests
    {
        private const float FrameDeltaTime = 1f / 60f;
        private const int MaximumTicks = 2000;

        private sealed class RevealRun
        {
            public BoardModel Model;
            public RevealTimeline Timeline;
            public RecordingRevealListener Listener;
            public bool SkippedBeforeLanding;
            public bool ScoreDroppedAfterLanding;
            public int Ticks;
        }

        private static RevealRun Run(int startRank, int targetRank, int skipAtTick)
        {
            BoardScene scene = LeaderboardScenario.Create(maximumAnimatedPasses: 15).LoadRevealScene(startRank, targetRank);
            var run = new RevealRun { Model = new BoardModel(scene, new MotionSettings()), Listener = new RecordingRevealListener() };
            run.Timeline = new RevealTimeline(run.Model, run.Listener);
            run.Timeline.Start();

            for (int tick = 0; tick < MaximumTicks && !run.Timeline.IsFinished; tick++)
            {
                if (tick == skipAtTick)
                {
                    run.SkippedBeforeLanding = !run.Timeline.HasReachedLanding;
                    run.Timeline.RequestSkip();
                }
                run.Timeline.Tick(FrameDeltaTime);
                run.Model.Advance(FrameDeltaTime);
                if (run.Timeline.HasReachedLanding && run.Model.LocalRow.DisplayScore != scene.Change.ToScore &&
                    scene.Change.Kind == RankChangeKind.RankUp)
                {
                    run.ScoreDroppedAfterLanding = true;
                }
                run.Ticks = tick + 1;
            }
            return run;
        }

        private static int TicksWithoutSkip(int startRank, int targetRank)
        {
            return Run(startRank, targetRank, -1).Ticks;
        }

        [TestCase(120, 108, TestName = "RankUp_SkipAtEveryTick_LandsWithPillAndFinalState")]
        [TestCase(900, 600, TestName = "RankUpCompressed_SkipAtEveryTick_LandsWithPillAndFinalState")]
        [TestCase(300, 5, TestName = "RankUpCompressedWithTail_SkipAtEveryTick_LandsWithPillAndFinalState")]
        public void RankUp_SkipAtEveryTick(int startRank, int targetRank)
        {
            int totalTicks = TicksWithoutSkip(startRank, targetRank);
            Assert.Greater(totalTicks, 10);

            for (int skipTick = 0; skipTick <= totalTicks; skipTick++)
            {
                RevealRun run = Run(startRank, targetRank, skipTick);
                string label = "skip@" + skipTick;
                RankChange change = run.Model.Scene.Change;
                RowState local = run.Model.LocalRow;

                Assert.IsTrue(run.Timeline.IsFinished, label + ": phải kết thúc");
                Assert.IsTrue(run.Timeline.HasReachedLanding, label + ": phải chạm nhịp hạ cánh");
                Assert.AreEqual(change.ToRank, local.DisplayRank, label + ": hạng cuối");
                Assert.AreEqual(change.ToScore, local.DisplayScore, label + ": điểm cuối");
                Assert.IsFalse(run.ScoreDroppedAfterLanding, label + ": điểm không được bị ghi đè sau hạ cánh");
                Assert.IsTrue(local.PillTiming.IsStarted, label + ": pill ▲N phải được bật");
                Assert.AreEqual(PillContent.RankUp, local.PillContent, label);
                Assert.AreEqual(change.PassedCount, local.PillValue, label + ": số trên pill");
                Assert.IsTrue(local.ShineTiming.IsStarted, label + ": vệt shine phải được bật");
                Assert.AreEqual(1, run.Listener.CountOf(LeaderboardBeat.Land), label + ": đúng 1 nhịp Land");
                Assert.AreEqual(1, run.Listener.LandedCount, label);
                Assert.AreEqual(run.SkippedBeforeLanding ? 1 : 0, run.Listener.CountOf(LeaderboardBeat.Skipped), label + ": nhịp Skipped");
                Assert.AreEqual(1, run.Listener.CountOf(LeaderboardBeat.RevealFinished), label);

                run.Model.FinishAllTweens();
                RankUpPlannerInvariantTests.AssertRowsSettled(run.Model, label);
            }
        }

        [Test]
        public void RankUp_WithoutSkip_EmitsPassBeatsThrottledAndNoSkippedBeat()
        {
            RevealRun run = Run(120, 108, -1);

            Assert.AreEqual(0, run.Listener.CountOf(LeaderboardBeat.Skipped));
            Assert.Greater(run.Listener.CountOf(LeaderboardBeat.Pass), 0);
            Assert.LessOrEqual(run.Listener.CountOf(LeaderboardBeat.Pass), run.Timeline.Plan.AnimatedCount);
            Assert.AreEqual(1, run.Listener.CountOf(LeaderboardBeat.Lift));
        }

        [Test]
        public void ReachFirstPlace_Celebrates()
        {
            RevealRun run = Run(10, 0, -1);

            Assert.AreEqual(RankTier.FirstPlace, run.Timeline.Tier);
            Assert.AreEqual(1, run.Listener.CelebrateCount);
            Assert.AreEqual(1, run.Listener.CountOf(LeaderboardBeat.Celebrate));
        }

        [Test]
        public void StandardRankUp_DoesNotCelebrate()
        {
            RevealRun run = Run(120, 108, -1);
            Assert.AreEqual(0, run.Listener.CelebrateCount);
        }

        [Test]
        public void CompressedJumpIntoTopList_RevealsTailAfterLanding()
        {
            // Nhảy vào trong top list: các row phía dưới đã có sẵn trong top nên bị tách tạm rồi gắn lại.
            // (Nhảy lớn ngoài top thì planner chỉ tải tới row cuối được diễn, không có tail.)
            RevealRun run = Run(300, 5, -1);

            Assert.IsTrue(run.Timeline.Plan.IsCompressed);
            Assert.Greater(run.Timeline.Plan.Tail.Count, 0);
            Assert.AreEqual(1, run.Listener.TailRevealedCount);
        }

        [Test]
        public void NewEntry_SkipAtEveryTick_ShowsNewPillAndFinalScore()
        {
            int totalTicks = TicksWithoutSkip(-1, 75);
            for (int skipTick = 0; skipTick <= totalTicks; skipTick++)
            {
                RevealRun run = Run(-1, 75, skipTick);
                RowState local = run.Model.LocalRow;
                string label = "skip@" + skipTick;

                Assert.IsTrue(run.Timeline.IsFinished, label);
                Assert.AreEqual(PillContent.New, local.PillContent, label);
                Assert.IsTrue(local.PillTiming.IsStarted, label);
                Assert.AreEqual(run.Model.Scene.Change.ToScore, local.DisplayScore, label);
                Assert.AreEqual(1f, local.Scale, 1e-4f, label);
                Assert.AreEqual(1, run.Listener.CountOf(LeaderboardBeat.NewEntry), label);
            }
        }

        [Test]
        public void Unchanged_BobsAndFinishesAtRestScale()
        {
            RevealRun run = Run(150, 150, -1);

            Assert.AreEqual(RankChangeKind.Unchanged, run.Model.Scene.Change.Kind);
            Assert.IsTrue(run.Timeline.IsFinished);
            Assert.AreEqual(1f, run.Model.LocalRow.Scale, 1e-4f);
            Assert.AreEqual(0, run.Listener.CountOf(LeaderboardBeat.Land));
        }

        [Test]
        public void ForceFinish_MidClimb_SettlesWithoutBeatsAndWithoutLanding()
        {
            BoardScene scene = LeaderboardScenario.Create(maximumAnimatedPasses: 15).LoadRevealScene(120, 108);
            var model = new BoardModel(scene, new MotionSettings());
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);
            timeline.Start();
            while (timeline.Phase != RevealPhase.Climb) { timeline.Tick(FrameDeltaTime); model.Advance(FrameDeltaTime); }
            timeline.Tick(0.2f);
            int beatsBefore = listener.Beats.Count;

            timeline.ForceFinish();

            Assert.IsTrue(timeline.IsFinished);
            Assert.IsFalse(timeline.HasReachedLanding);
            Assert.AreEqual(beatsBefore, listener.Beats.Count);
            RankUpPlannerInvariantTests.AssertRowsSettled(model, "force-finish");
        }

        [Test]
        public void HugeDeltaTime_RunsThroughAllPhasesInOneTick()
        {
            BoardScene scene = LeaderboardScenario.Create(maximumAnimatedPasses: 15).LoadRevealScene(120, 108);
            var model = new BoardModel(scene, new MotionSettings());
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Start();

            timeline.Tick(30f);

            Assert.IsTrue(timeline.IsFinished);
            Assert.AreEqual(scene.Change.ToRank, model.LocalRow.DisplayRank);
        }
    }
}
