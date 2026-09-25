using System;
using System.Collections.Generic;
using DreamTech.Leaderboard.ViewModel;
using NUnit.Framework;

namespace DreamTech.Leaderboard.Tests
{
    /// <summary>
    /// Các cờ opt-in 0.4.0 tái hiện màn xếp hạng sau thắng của một game tham chiếu: người bị vượt đứng yên tới lúc đáp, tick theo đồng
    /// hồ, glow theo đồng hồ riêng, nhịp nhẹ khi không đổi hạng, độ trễ lệch đầu của đợt trượt vào. Cờ nào cũng mặc định tắt
    /// (test "vân tay" ở <c>RevealTimelineFlagOffTests</c> canh phía tắt); ở đây canh phía BẬT.
    /// </summary>
    [TestFixture]
    public class RevealTimelineReferenceMotionTests
    {
        private const float FrameDeltaTime = 1f / 60f;
        private const int MaximumTicks = 4000;

        /// <summary>Cuộn 2500 px/s trên hàng 224 px ⇒ 0,0896 s mỗi hàng, không sàn, không trần.</summary>
        private static MotionSettings ReferenceClimb()
        {
            return new MotionSettings
            {
                ClimbSecondsPerRow = 224f / 2500f,
                ClimbDurationMinimum = 0f,
                ClimbDurationMaximum = 100f,
                LiftDuration = 0.2f,
                LiftScale = 1.05f,
                LandDuration = 0.22f,
            };
        }

        private static BoardModel NewModel(MotionSettings settings, int startRank, int targetRank)
        {
            BoardScene scene = LeaderboardScenario.Create(maximumAnimatedPasses: 60).LoadRevealScene(startRank, targetRank);
            return new BoardModel(scene, settings);
        }

        private static void Tick(RevealTimeline timeline, BoardModel model, int ticks)
        {
            for (int index = 0; index < ticks && !timeline.IsFinished; index++)
            {
                timeline.Tick(FrameDeltaTime);
                model.Advance(FrameDeltaTime);
            }
        }

        private static void RunUntil(RevealTimeline timeline, BoardModel model, Func<bool> condition)
        {
            for (int index = 0; index < MaximumTicks && !condition(); index++)
            {
                timeline.Tick(FrameDeltaTime);
                model.Advance(FrameDeltaTime);
            }
            Assert.IsTrue(condition(), "Điều kiện không bao giờ tới.");
        }

        [Test]
        public void DeferPassSlides_PassedRowsStayPut_UntilLanding_ThenSlideOneSlotLinearly()
        {
            MotionSettings settings = ReferenceClimb();
            settings.DeferPassSlidesToLand = true;
            BoardModel model = NewModel(settings, 120, 108);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Start();

            IReadOnlyList<RowState> passed = null;
            var startSlots = new List<float>();
            var startRanks = new List<int>();
            RunUntil(timeline, model, () => timeline.Phase == RevealPhase.Climb);
            passed = timeline.Plan.Passed;
            foreach (RowState row in passed)
            {
                startSlots.Add(row.Slot);
                startRanks.Add(row.DisplayRank);
            }

            RunUntil(timeline, model, () => timeline.Phase == RevealPhase.Land);
            for (int index = 0; index < passed.Count; index++)
            {
                Assert.AreEqual(startRanks[index], passed[index].DisplayRank, "Người bị vượt đổi số trước lúc chốt.");
            }

            // 0,06 s vào pha đáp: đi được đúng nửa đường 0,12 s — tuyến tính, không OutCubic.
            Tick(timeline, model, 3);
            float halfway = passed[0].Slot - startSlots[0];
            Assert.AreEqual(0.5f, halfway, 0.1f, "Người bị vượt không dời đều trong 0,12 s.");

            RunUntil(timeline, model, () => timeline.IsFinished);
            for (int index = 0; index < passed.Count; index++)
            {
                Assert.AreEqual(startSlots[index] + 1f, passed[index].Slot, 0.0001f);
                Assert.AreEqual(passed[index].Entry.Rank, passed[index].DisplayRank);
            }
        }

        [Test]
        public void DeferPassSlides_WhileClimbing_NoPassedRowMoves()
        {
            MotionSettings settings = ReferenceClimb();
            settings.DeferPassSlidesToLand = true;
            BoardModel model = NewModel(settings, 120, 108);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Start();
            RunUntil(timeline, model, () => timeline.Phase == RevealPhase.Climb);

            var slots = new Dictionary<RowState, float>();
            foreach (RowState row in timeline.Plan.Passed) slots[row] = row.Slot;
            while (timeline.Phase == RevealPhase.Climb)
            {
                Tick(timeline, model, 1);
                if (timeline.Phase != RevealPhase.Climb) break; // tick vừa rồi đã sang pha đáp — nơi họ ĐƯỢC dời
                foreach (KeyValuePair<RowState, float> pair in slots) Assert.AreEqual(pair.Value, pair.Key.Slot, 0.0001f);
            }
        }

        [Test]
        public void ClimbTickInterval_TicksOnTheClock_NotPerPass()
        {
            MotionSettings settings = ReferenceClimb();
            settings.ClimbTickInterval = 0.18f;
            BoardModel model = NewModel(settings, 120, 108);
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);
            timeline.Start();
            RunUntil(timeline, model, () => timeline.IsFinished);

            // 12 hàng × 0,0896 = 1,075 s ⇒ cửa sổ 0,895 s ⇒ tick ở 0; 0,18; 0,36; 0,54; 0,72 = 5 tick (không phải 12).
            Assert.AreEqual(5, listener.CountOf(LeaderboardBeat.Pass));
        }

        [Test]
        public void GlowEnvelope_FadesIn_Holds_ThenFadesOutAfterLanding_EvenAfterFinish()
        {
            MotionSettings settings = ReferenceClimb();
            settings.GlowFadeInDuration = 0.133f;
            settings.GlowFadeOutDelay = 0.15f;
            settings.GlowFadeOutDuration = 0.5f;
            BoardModel model = NewModel(settings, 120, 108);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Start();
            RowState local = model.LocalRow;

            RunUntil(timeline, model, () => timeline.Phase == RevealPhase.Climb);
            Tick(timeline, model, 12);
            Assert.AreEqual(1f, local.GlowBoost, 0.001f, "Glow không giữ sáng suốt cú leo.");

            RunUntil(timeline, model, () => timeline.IsFinished);
            Assert.Greater(local.GlowBoost, 0.9f, "Glow tắt ngay lúc màn diễn kết thúc — phải còn sáng tới +0,15 s sau khi đáp.");

            for (int index = 0; index < 60; index++) model.Advance(FrameDeltaTime);
            Assert.AreEqual(0f, local.GlowBoost, 0.0001f);
            Assert.IsFalse(local.IsGlowEnvelopeActive);
        }

        [Test]
        public void ForceFinish_KillsTheGlowEnvelope()
        {
            MotionSettings settings = ReferenceClimb();
            settings.GlowFadeInDuration = 0.133f;
            BoardModel model = NewModel(settings, 120, 108);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Start();
            RunUntil(timeline, model, () => timeline.Phase == RevealPhase.Climb);

            timeline.ForceFinish();
            Assert.AreEqual(0f, model.LocalRow.GlowBoost);
            Assert.IsFalse(model.LocalRow.IsGlowEnvelopeActive);
        }

        [Test]
        public void QuietPulse_ScoredButSameRank_LiftsAndLands_WithoutGlow()
        {
            MotionSettings settings = ReferenceClimb();
            settings.QuietPulseInsteadOfBob = true;
            settings.HostOwnsScoreCount = true;
            BoardScene scene = LeaderboardScenario.Create(maximumAnimatedPasses: 60).LoadScoreImprovedScene(120);
            Assert.AreEqual(RankChangeKind.ScoreImproved, scene.Change.Kind);
            var model = new BoardModel(scene, settings);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Start();
            RunUntil(timeline, model, () => timeline.Phase == RevealPhase.Bob);

            float peak = 0f;
            int ticks = 0;
            while (!timeline.IsFinished && ticks++ < MaximumTicks)
            {
                Tick(timeline, model, 1);
                peak = Math.Max(peak, model.LocalRow.Scale);
                Assert.AreEqual(0f, model.LocalRow.GlowBoost, "Nhịp nhẹ không được sáng glow.");
            }
            Assert.AreEqual(1.05f, peak, 0.01f);
            Assert.AreEqual(1f, model.LocalRow.Scale, 0.0001f);
            Assert.AreEqual(0.42f, ticks * FrameDeltaTime, 0.05f, "Nhịp nhẹ phải dài đúng nhấc 0,2 + đáp 0,22.");
        }

        /// <summary>Lượt 0 điểm thì bảng đứng yên — không nhấc, không glow, xong ngay.</summary>
        [Test]
        public void QuietPulse_ZeroScore_StaysStill_AndFinishesAtOnce()
        {
            MotionSettings settings = ReferenceClimb();
            settings.QuietPulseInsteadOfBob = true;
            BoardModel model = NewModel(settings, 120, 120);
            Assert.AreEqual(RankChangeKind.Unchanged, model.Scene.Change.Kind);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Start();

            // Khoảng chờ đầu (IntroWait) là của mọi lượt; tính từ lúc nó hết.
            int ticks = 0;
            int ticksAfterIntro = 0;
            while (!timeline.IsFinished && ticks++ < MaximumTicks)
            {
                if (timeline.Phase != RevealPhase.Intro) ticksAfterIntro++;
                Tick(timeline, model, 1);
                Assert.AreEqual(1f, model.LocalRow.Scale, 0.0001f, "Lượt 0 điểm không được nhấc hàng.");
                Assert.AreEqual(0f, model.LocalRow.GlowBoost);
            }
            Assert.IsTrue(timeline.IsFinished);
            Assert.LessOrEqual(ticksAfterIntro, 1, "Lượt 0 điểm phải chốt ngay khi hết khoảng chờ đầu.");
        }

        [Test]
        public void IntroRowDelayOffset_DelaysEveryRow()
        {
            var settings = new MotionSettings { IntroRowDelayOffset = 0.06f, IntroDuration = 0.5f, IntroOffsetX = 1080f };
            BoardModel model = NewModel(settings, 120, 108);
            model.StartIntro(model.Rows[0].Slot);

            model.Advance(0.05f);
            Assert.AreEqual(1080f, model.Rows[0].IntroOffsetX, 0.001f, "Row đầu đã trượt trước độ trễ lệch đầu.");
            model.Advance(0.05f);
            Assert.Less(model.Rows[0].IntroOffsetX, 1080f);
        }

        [Test]
        public void Curves_DriveLiftClimbAndLand()
        {
            MotionSettings settings = ReferenceClimb();
            settings.LiftCurve = new KeyframeCurve(new KeyframeCurve.Key(0f, 0f, 0f, 0f), new KeyframeCurve.Key(1f, 1f, 0f, 0f));
            settings.ClimbCurve = new KeyframeCurve(new KeyframeCurve.Key(0f, 0f, 1f, 1f), new KeyframeCurve.Key(1f, 1f, 1f, 1f));
            settings.LandCurve = new KeyframeCurve(new KeyframeCurve.Key(0f, 0f, 0f, 0f), new KeyframeCurve.Key(0.5f, 2f, 0f, 0f),
                                                   new KeyframeCurve.Key(1f, 1f, 0f, 0f));
            BoardModel model = NewModel(settings, 120, 108);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Start();

            RunUntil(timeline, model, () => timeline.Phase == RevealPhase.Climb);
            float startSlot = timeline.Plan.StartSlot;
            float duration = RankUpPlanner.ClimbDuration(12, settings);
            Tick(timeline, model, (int)Math.Round(duration * 0.5f / FrameDeltaTime));
            Assert.AreEqual(startSlot - 6f, model.LocalRow.Slot, 0.35f, "Đường cong leo tuyến tính mà giữa đường không ở nửa quãng.");

            RunUntil(timeline, model, () => timeline.Phase == RevealPhase.Land);
            float minimum = float.MaxValue;
            while (timeline.Phase == RevealPhase.Land)
            {
                Tick(timeline, model, 1);
                minimum = Math.Min(minimum, model.LocalRow.Scale);
            }
            // lerp(1,05; 1; 2) = 0,95 ở giữa cú đáp: đường cong vượt ra ngoài [0,1] phải đi thẳng vào cỡ.
            Assert.AreEqual(0.95f, minimum, 0.01f);
        }
    }
}
