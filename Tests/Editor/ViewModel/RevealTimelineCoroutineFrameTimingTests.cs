using System.Collections.Generic;
using DreamTech.Leaderboard.ViewModel;
using NUnit.Framework;

namespace DreamTech.Leaderboard.Tests
{
    /// <summary>
    /// Cờ opt-in <see cref="MotionSettings.CoroutineFrameTiming"/> (0.6.0): các pha đi theo nhịp khung của một coroutine
    /// <c>elapsed += dt; vẽ; yield</c> — khung đầu của pha đã tính độ dài của chính nó, pha kế bắt đầu ở khung SAU mẫu cuối, và
    /// tick theo đồng hồ hẹn lại từ khung nó nổ. Cờ tắt (mặc định) thì y như trước — các dấu vân tay ở
    /// <c>RevealTimelineFlagOffTests</c> canh phía đó.
    ///
    /// <para><b>Bước khung.</b> Phần lớn dùng 1/64 s và thời lượng là bội của nó để phép cộng dồn float không lệch một bit; phần
    /// tick dùng 1/60 s và 0,18 s như game tham chiếu (11 khung mỗi tick).</para>
    /// </summary>
    [TestFixture]
    public class RevealTimelineCoroutineFrameTimingTests
    {
        private const float ExactDelta = 1f / 64f;
        private const float ExactLiftDuration = 12f / 64f;
        private const float ReferenceDelta = 1f / 60f;
        private const float ReferenceClimbTickInterval = 0.18f;
        private const int MaximumTicks = 4000;
        private const float PatientTimeout = 100f;

        /// <summary>Tốc độ cuộn của cú tiếp cận bục và quãng cuộn khi xuất phát ở hạng 4 (bố cục của game tham chiếu).</summary>
        private const float ReferenceScrollSpeed = 2500f;

        private const float ReferenceRankFourStartScroll = 242.95f;

        private static MotionSettings Settings(bool coroutineFrameTiming, bool waitsForHost = true)
        {
            return new MotionSettings
            {
                WaitForHostRelease = waitsForHost,
                HostHoldTimeout = PatientTimeout,
                IntroWait = 4f * ExactDelta,
                LiftDuration = ExactLiftDuration,
                LiftScale = 1.05f,
                ClimbSecondsPerRow = 0.09f,
                ClimbDurationMinimum = 0f,
                ClimbDurationMaximum = 100f,
                ClimbTickInterval = ReferenceClimbTickInterval,
                LandDuration = 0.22f,
                HostOwnsScoreCount = true,
                CoroutineFrameTiming = coroutineFrameTiming,
            };
        }

        private static BoardModel NewModel(int startRank, int targetRank, MotionSettings settings)
        {
            BoardScene scene = LeaderboardScenario.Create(maximumAnimatedPasses: 60).LoadRevealScene(startRank, targetRank);
            return new BoardModel(scene, settings);
        }

        private static void Step(RevealTimeline timeline, BoardModel model, float deltaTime)
        {
            timeline.Tick(deltaTime);
            model.Advance(deltaTime);
        }

        /// <summary>Chạy tới lúc màn diễn đứng chờ host (đã qua <c>IntroWait</c>, đang ở nhịp chờ).</summary>
        private static void RunToHostGate(RevealTimeline timeline, BoardModel model)
        {
            for (int tick = 0; tick < 20; tick++) Step(timeline, model, ExactDelta);
            Assert.AreEqual(RevealPhase.Intro, timeline.Phase, "Tiền đề hỏng: host chưa thả thì phải còn đứng ở cổng Intro.");
        }

        // ---------------------------------------------------------------- Mặc định

        [Test]
        public void Defaults_CoroutineFrameTimingIsOff()
        {
            Assert.IsFalse(new MotionSettings().CoroutineFrameTiming);
        }

        /// <summary>Widget chụp settings bằng <c>Clone()</c>: cờ phải đi theo.</summary>
        [Test]
        public void Clone_CarriesCoroutineFrameTiming()
        {
            Assert.IsTrue(Settings(coroutineFrameTiming: true).Clone().CoroutineFrameTiming);
        }

        // ---------------------------------------------------------------- Khung đầu

        /// <summary>
        /// Host thả cổng: ngay tick đó pha Lift vẽ ở giây dt (không phải giây 0) — như coroutine được gọi thẳng từ callback.
        /// </summary>
        [Test]
        public void ReleasedGate_TheLiftSamplesTheReleaseFramesDelta()
        {
            MotionSettings settings = Settings(coroutineFrameTiming: true);
            BoardModel model = NewModel(42, 12, settings);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Start();
            RunToHostGate(timeline, model);

            timeline.ReleaseHostHold();
            Step(timeline, model, ExactDelta);

            Assert.AreEqual(RevealPhase.Lift, timeline.Phase);
            float expected = Easing.LerpUnclamped(1f, settings.LiftScale, Easing.OutCubic(ExactDelta / ExactLiftDuration));
            Assert.AreEqual(expected, model.LocalRow.Scale, 1e-6f, "Khung thả cổng phải vẽ Lift ở giây dt.");
        }

        /// <summary>
        /// Pha dài n khung vẽ mẫu cuối ở khung thứ n và trao cho pha kế ở khung n + 1 — nhịp đầu của pha kế (Pass) phát ở khung
        /// đó, không phát ở khung mẫu cuối.
        /// </summary>
        [Test]
        public void Phase_HandsOverOnTheFrameAfterItsLastSample()
        {
            MotionSettings settings = Settings(coroutineFrameTiming: true);
            BoardModel model = NewModel(42, 12, settings);
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);
            timeline.Start();
            RunToHostGate(timeline, model);
            timeline.ReleaseHostHold();

            for (int frame = 1; frame <= 12; frame++) Step(timeline, model, ExactDelta);

            Assert.AreEqual(RevealPhase.Lift, timeline.Phase, "Khung thứ 12 (mẫu cuối) vẫn phải là Lift.");
            Assert.AreEqual(settings.LiftScale, model.LocalRow.Scale, 1e-6f, "Khung thứ 12 vẽ mẫu cuối của Lift.");
            Assert.AreEqual(0, listener.CountOf(LeaderboardBeat.Pass));

            Step(timeline, model, ExactDelta);

            Assert.AreEqual(RevealPhase.Climb, timeline.Phase, "Khung thứ 13 mới sang Climb.");
            Assert.AreEqual(1, listener.CountOf(LeaderboardBeat.Pass), "Nhịp đầu của Climb phát ở khung nó bắt đầu.");
        }

        /// <summary>
        /// Cú tiếp cận bục (pha Climb kiểu cuộn): khung đầu đã cuộn một quãng (tiến độ &gt; 0), khung cuối chạm 1 mà vẫn ở Climb,
        /// khung sau mới là cổng bục với nhịp PodiumTakeover — hạng 4 ở 60 Hz: sáu khung cuộn rồi mới thu hàng.
        /// </summary>
        [Test]
        public void PodiumApproach_FirstFrameScrolls_TakeoverOnTheFrameAfterTheLastSample()
        {
            MotionSettings settings = Settings(coroutineFrameTiming: true, waitsForHost: false);
            settings.HostPresentedTopRanks = 3;
            settings.DeferPodiumApproachPasses = true;
            settings.PodiumApproachScrollSpeed = ReferenceScrollSpeed;
            BoardModel model = NewModel(3, 0, settings);
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);
            timeline.Prepare();
            model.SetPodiumApproachStartScroll(ReferenceRankFourStartScroll);
            timeline.Start();

            for (int tick = 0; tick < MaximumTicks && timeline.Phase != RevealPhase.Climb; tick++)
            {
                Step(timeline, model, ReferenceDelta);
            }
            Assert.AreEqual(RevealPhase.Climb, timeline.Phase);
            Assert.Greater(model.PodiumApproachProgress, 0f, "Khung đầu của cú tiếp cận phải đã cuộn.");

            int climbFrames = 1;
            while (timeline.Phase == RevealPhase.Climb && model.PodiumApproachProgress < 1f && climbFrames < MaximumTicks)
            {
                Step(timeline, model, ReferenceDelta);
                climbFrames++;
            }
            Assert.AreEqual(RevealPhase.Climb, timeline.Phase, "Khung chạm tiến độ 1 vẫn phải là Climb.");
            Assert.AreEqual(6, climbFrames, "Quãng 242,95 / 2500 = 0,0972 s = 6 khung ở 60 Hz.");
            Assert.AreEqual(0, listener.CountOf(LeaderboardBeat.PodiumTakeover));

            Step(timeline, model, ReferenceDelta);

            Assert.AreEqual(RevealPhase.PodiumHold, timeline.Phase);
            Assert.AreEqual(1, listener.CountOf(LeaderboardBeat.PodiumTakeover), "Thu hàng ở khung sau mẫu cuối.");
        }

        // ---------------------------------------------------------------- Tick theo đồng hồ

        /// <summary>
        /// 0,18 s ở 60 Hz: cờ bật thì mọi khoảng giữa hai tick là 11 khung (đồng hồ hẹn lại từ khung nó nổ); cờ tắt thì tick bám
        /// k × 0,18 s nên có khoảng 10 khung (0,9 s = đúng 54 khung).
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ClimbTicks_SpacingAtSixtyHertz(bool coroutineFrameTiming)
        {
            MotionSettings settings = Settings(coroutineFrameTiming, waitsForHost: false);
            BoardModel model = NewModel(42, 12, settings);
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);
            timeline.Start();

            var tickFrames = new List<int>();
            for (int frame = 0; frame < MaximumTicks && timeline.Phase != RevealPhase.Finished; frame++)
            {
                int before = listener.CountOf(LeaderboardBeat.Pass);
                Step(timeline, model, ReferenceDelta);
                if (listener.CountOf(LeaderboardBeat.Pass) > before) tickFrames.Add(frame);
            }

            Assert.GreaterOrEqual(tickFrames.Count, 7, "Tiền đề hỏng: cú leo phải đủ dài cho ít nhất 7 tick.");
            var spacings = new List<int>();
            for (int index = 1; index < tickFrames.Count; index++) spacings.Add(tickFrames[index] - tickFrames[index - 1]);
            if (coroutineFrameTiming)
            {
                foreach (int spacing in spacings) Assert.AreEqual(11, spacing, "Cờ bật: mỗi tick cách tick trước đúng 11 khung.");
            }
            else
            {
                CollectionAssert.Contains(spacings, 10, "Cờ tắt: tick bám k × 0,18 s nên có khoảng 10 khung.");
            }
        }

        // ---------------------------------------------------------------- Không đổi gì khác

        /// <summary>Cùng một cú leo trong list: bật hay tắt cờ thì chuỗi nhịp và trạng thái cuối như nhau.</summary>
        [Test]
        public void SameBeatsAndFinalState_AsTheContinuousClock()
        {
            List<LeaderboardBeat> BeatsOf(bool coroutineFrameTiming, out float finalSlot, out int finalRank)
            {
                BoardModel model = NewModel(42, 12, Settings(coroutineFrameTiming, waitsForHost: false));
                var listener = new RecordingRevealListener();
                var timeline = new RevealTimeline(model, listener);
                timeline.Start();
                for (int tick = 0; tick < MaximumTicks && timeline.Phase != RevealPhase.Finished; tick++)
                {
                    Step(timeline, model, ExactDelta);
                }
                Assert.AreEqual(RevealPhase.Finished, timeline.Phase);
                finalSlot = model.LocalRow.Slot;
                finalRank = model.LocalRow.DisplayRank;
                return listener.Beats;
            }

            List<LeaderboardBeat> withFlag = BeatsOf(true, out float slotOn, out int rankOn);
            List<LeaderboardBeat> withoutFlag = BeatsOf(false, out float slotOff, out int rankOff);

            CollectionAssert.AreEqual(withoutFlag, withFlag);
            Assert.AreEqual(slotOff, slotOn, 1e-5f);
            Assert.AreEqual(rankOff, rankOn);
        }

        // ---------------------------------------------------------------- Lưới khung của tick (ClimbTickFrameRate)

        /// <summary>Tốc độ lưới của game tham chiếu (bước cố định 1/60).</summary>
        private const float ReferenceFrameRate = 60f;

        /// <summary>Khoảng tick trên lưới: 0,18 s làm tròn lên 11 khung = 11/60 s.</summary>
        private const float GridTickInterval = 11f / 60f;

        [Test]
        public void ClimbTickFrameRate_DefaultsOff_AndCloneCarriesIt()
        {
            Assert.AreEqual(0f, new MotionSettings().ClimbTickFrameRate);
            MotionSettings settings = Settings(coroutineFrameTiming: true);
            settings.ClimbTickFrameRate = ReferenceFrameRate;
            Assert.AreEqual(ReferenceFrameRate, settings.Clone().ClimbTickFrameRate);
        }

        /// <summary>Khung đều 1/60: lưới cho đúng 11 khung mỗi tick — trùng cách hẹn lại, cùng số tick.</summary>
        [Test]
        public void ClimbTickFrameRate_AtSixtyHertz_ElevenFramesPerTick_SameCountAsRearm()
        {
            List<float> rearmed = ClimbTickTimes(frameRate: 0f, ReferenceDelta);
            List<float> gridded = ClimbTickTimes(ReferenceFrameRate, ReferenceDelta);

            Assert.AreEqual(rearmed.Count, gridded.Count, "Lưới không đổi số tick.");
            for (int index = 1; index < gridded.Count; index++)
            {
                Assert.AreEqual(GridTickInterval, gridded[index] - gridded[index - 1], ReferenceDelta * 0.25f,
                                "Tick " + index + " cách tick trước đúng 11 khung.");
            }
        }

        /// <summary>
        /// Đồng hồ nhanh hơn 60 Hz một chút (khung 1/61,5 s): cách hẹn lại từ khung nổ cần khung thứ 12 ở vài tick và độ trễ CỘNG DỒN;
        /// lưới giữ tick thứ k trong một khung của k × 11/60.
        /// </summary>
        [Test]
        public void ClimbTickFrameRate_OnAFasterClock_DoesNotDrift()
        {
            const float fastDelta = 1f / 61.5f;
            List<float> rearmed = ClimbTickTimes(frameRate: 0f, fastDelta);
            List<float> gridded = ClimbTickTimes(ReferenceFrameRate, fastDelta);

            Assert.GreaterOrEqual(gridded.Count, 7, "Tiền đề hỏng: cú leo phải đủ dài cho ít nhất 7 tick.");
            float rearmedDrift = rearmed[rearmed.Count - 1] - rearmed[0] - (rearmed.Count - 1) * GridTickInterval;
            Assert.Greater(rearmedDrift, fastDelta, "Tiền đề hỏng: cách hẹn lại phải trôi quá một khung trên đồng hồ này.");
            for (int index = 0; index < gridded.Count; index++)
            {
                float target = gridded[0] + index * GridTickInterval;
                Assert.AreEqual(target, gridded[index], fastDelta, "Tick " + index + " phải nằm trong một khung của mốc lưới.");
            }
        }

        /// <summary>Giây (tính từ lúc bắt đầu) của từng tick Pass trong một cú leo 42 → 12, với lưới <paramref name="frameRate"/>.</summary>
        private static List<float> ClimbTickTimes(float frameRate, float deltaTime)
        {
            MotionSettings settings = Settings(coroutineFrameTiming: true, waitsForHost: false);
            settings.ClimbTickFrameRate = frameRate;
            BoardModel model = NewModel(42, 12, settings);
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);
            timeline.Start();

            var times = new List<float>();
            float clock = 0f;
            for (int frame = 0; frame < MaximumTicks && timeline.Phase != RevealPhase.Finished; frame++)
            {
                int before = listener.CountOf(LeaderboardBeat.Pass);
                Step(timeline, model, deltaTime);
                clock += deltaTime;
                if (listener.CountOf(LeaderboardBeat.Pass) > before) times.Add(clock);
            }
            return times;
        }
    }
}
