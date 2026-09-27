using System;
using System.Collections.Generic;
using DreamTech.Leaderboard.ViewModel;
using NUnit.Framework;

namespace DreamTech.Leaderboard.Tests
{
    /// <summary>
    /// Ba cờ opt-in của 0.6.0 cho màn lên bục theo một game tham chiếu, phía view-model:
    /// <list type="bullet">
    /// <item><see cref="MotionSettings.DeferPodiumApproachPasses"/>: người bị vượt ở đoạn trong list đứng yên tới tick host thả
    /// cổng, rồi cả bảng về trạng thái cuối trong đúng tick đó.</item>
    /// <item><see cref="MotionSettings.PodiumApproachScrollSpeed"/>: cú tiếp cận ranh giới dài đúng quãng cuộn / tốc độ, luôn có
    /// (kể cả bắt đầu ở ranh giới), camera và Slot đi theo cùng một tiến độ.</item>
    /// <item><see cref="MotionSettings.HostPresentedRowSkipsQuietPulse"/>: lượt không đổi chỗ của một row đang trên bục không có
    /// cú nhún vô hình.</item>
    /// </list>
    /// Kèm hai điều kiểm của cú lên bục (P5): số hạng cuối hiện từ lúc nhấc, không quay số; hào quang và dòng mũi tên chạy tới
    /// đúng nhịp PodiumTakeover. Cờ tắt (mặc định) thì y như 0.5.0 — các dấu vân tay ở <c>RevealTimelineFlagOffTests</c> canh phía
    /// đó, ở đây canh phía BẬT.
    ///
    /// <para><b>Số tham chiếu.</b> Hàng cao 218, bước 224, vùng bục 638 (lề trên list −34), khung nhìn 1030,1 trên màn 1080×1920,
    /// tốc độ cuộn 2500 — số của game tham chiếu mà Icon Match (Golden Race) bật các cờ này. Ô xuất phát hạng n canh giữa ở quãng
    /// cuộn 231,95 + (n − 4) × 224 ⇒ thời lượng tiếp cận 0,0928 s (hạng 4), 0,1824 (5), 0,8096 (12), 1,5264 (20), 1,9744 (25) với
    /// 1 / 1 / 4 / 8 / 10 nhịp tick 0,18 s.</para>
    /// </summary>
    [TestFixture]
    public class RevealTimelinePodiumApproachTests
    {
        private const float FrameDeltaTime = 1f / 60f;
        private const int MaximumTicks = 4000;
        private const int TopRanks = 3;
        private const float PatientTimeout = 100f;

        /// <summary>Tốc độ cuộn danh sách của game tham chiếu (đơn vị canvas / giây).</summary>
        private const float ReferenceScrollSpeed = 2500f;

        /// <summary>Nhịp tick + rung khi cuộn của game tham chiếu.</summary>
        private const float ReferenceClimbTickInterval = 0.18f;

        /// <summary>Hàng 218 + khe 6 = bước 224; vùng bục 638 ⇒ lề trên list 638 − 3 × 224 = −34.</summary>
        private const float ReferenceRowHeight = 218f;

        private const float ReferenceRowSpacing = 6f;
        private const float ReferenceTopPadding = -34f;
        private const float ReferenceBottomPadding = 72f;

        /// <summary>Khung nhìn danh sách trên màn 1080×1920: 1920 − 889,9.</summary>
        private const float ReferenceViewportHeight = 1030.1f;

        /// <summary>Dung sai của thời lượng tiếp cận (bắt kẹp hai phía bằng hai tick đúng mốc).</summary>
        private const float DurationTolerance = 0.0001f;

        /// <summary>Đường cuộn của game tham chiếu: hai khoá, khoá cuối có trọng số phía vào.</summary>
        private static KeyframeCurve ReferenceScrollCurve()
        {
            return new KeyframeCurve(new KeyframeCurve.Key(0f, 0f, 0f, 0f),
                                     new KeyframeCurve.Key(1f, 1f, 0.09481128f, 0.09481128f, KeyframeCurve.WeightedMode.In,
                                                           0.47263688f, 0f));
        }

        private static readonly VirtualListLayout ReferenceLayout =
            new VirtualListLayout(ReferenceRowHeight, ReferenceRowSpacing, ReferenceTopPadding, ReferenceBottomPadding);

        /// <summary>Cấu hình kiểu host (lật hạng sớm, tick theo đồng hồ, glow theo đồng hồ riêng) + ba cờ 0.6.0.</summary>
        private static MotionSettings ApproachSettings(bool defersApproachPasses = true, float scrollSpeed = ReferenceScrollSpeed,
                                                       bool skipsHostPresentedPulse = true)
        {
            return new MotionSettings
            {
                HostPresentedTopRanks = TopRanks,
                HostHoldTimeout = PatientTimeout,
                LiftDuration = 0.2f,
                LiftScale = 1.05f,
                ClimbSecondsPerRow = (ReferenceRowHeight + ReferenceRowSpacing) / ReferenceScrollSpeed,
                ClimbDurationMinimum = 0f,
                ClimbDurationMaximum = 100f,
                ClimbCurve = ReferenceScrollCurve(),
                ClimbTickInterval = ReferenceClimbTickInterval,
                LandDuration = 0.22f,
                DeferPassSlidesToLand = true,
                RankFlipsBeforeRankMove = true,
                RankRollDuration = 0.001f,
                GlowFadeInDuration = 0.133f,
                GlowFadeOutDelay = 0.15f,
                GlowFadeOutDuration = 0.5f,
                QuietPulseInsteadOfBob = true,
                HostOwnsScoreCount = true,
                DeferPodiumApproachPasses = defersApproachPasses,
                PodiumApproachScrollSpeed = scrollSpeed,
                HostPresentedRowSkipsQuietPulse = skipsHostPresentedPulse,
            };
        }

        private static BoardModel NewModel(int startRank, int targetRank, MotionSettings settings)
        {
            BoardScene scene = LeaderboardScenario.Create(maximumAnimatedPasses: 60).LoadRevealScene(startRank, targetRank);
            return new BoardModel(scene, settings);
        }

        /// <summary>Quãng cuộn mà list của Golden Race báo cho ô xuất phát (canh giữa ô đó trong khung nhìn 1030,1).</summary>
        private static float ReferenceStartScroll(float startSlot)
        {
            return ReferenceLayout.CenteredScroll(startSlot, ReferenceViewportHeight, float.MaxValue);
        }

        private static void Step(RevealTimeline timeline, BoardModel model, float deltaTime)
        {
            timeline.Tick(deltaTime);
            model.Advance(deltaTime);
        }

        private static void RunUntil(RevealTimeline timeline, BoardModel model, Func<bool> condition)
        {
            for (int tick = 0; tick < MaximumTicks && !condition(); tick++) Step(timeline, model, FrameDeltaTime);
            Assert.IsTrue(condition(), "Điều kiện không bao giờ tới.");
        }

        /// <summary>
        /// Tới ĐÚNG đầu cú tiếp cận: một tick dài bằng Intro rồi một tick dài bằng Lift — phần dư chuyển sang pha kế là 0, nên
        /// sau đó mọi mốc của cú tiếp cận đo được bằng tick có độ dài chọn trước.
        /// </summary>
        private static void AdvanceToApproachStart(RevealTimeline timeline, BoardModel model)
        {
            Step(timeline, model, model.Settings.IntroWait);
            Assert.AreEqual(RevealPhase.Lift, timeline.Phase, "Tiền đề hỏng: hết Intro phải là Lift.");
            Step(timeline, model, model.Settings.LiftDuration);
        }

        /// <summary>
        /// Đo thời lượng cú tiếp cận bằng kẹp hai phía: còn ở Climb sau (dự kiến − dung sai), đã tới cổng bục sau thêm hai lần
        /// dung sai.
        /// </summary>
        private static void AssertApproachLasts(RevealTimeline timeline, BoardModel model, float expectedDuration, string label)
        {
            AdvanceToApproachStart(timeline, model);
            Assert.AreEqual(RevealPhase.Climb, timeline.Phase, label + ": phải đang ở cú tiếp cận.");
            Step(timeline, model, expectedDuration - DurationTolerance);
            Assert.AreEqual(RevealPhase.Climb, timeline.Phase, label + ": cú tiếp cận xong SỚM hơn quãng cuộn / tốc độ.");
            Step(timeline, model, 2f * DurationTolerance);
            Assert.AreEqual(RevealPhase.PodiumHold, timeline.Phase, label + ": cú tiếp cận chưa xong sau quãng cuộn / tốc độ.");
            Assert.AreEqual(1f, model.PodiumApproachProgress, 1e-6f, label + ": hết cú tiếp cận thì tiến độ phải là 1.");
        }

        // ---------------------------------------------------------------- PodiumApproachScrollSpeed

        /// <summary>
        /// Thời lượng tiếp cận = quãng cuộn / 2500, KHÔNG kẹp, ở khung nhìn 1030,1: đúng các số đo trên phim của game tham chiếu
        /// (hạng 4 → 0,0928 s cho tới hạng 25 → 1,9744 s, sai ≤ 1e-4 s).
        /// </summary>
        [TestCase(3, 0, 0.0928f, TestName = "PodiumApproach_Rank4_LastsTheScrollDistanceOverTheSpeed")]
        [TestCase(4, 1, 0.1824f, TestName = "PodiumApproach_Rank5_LastsTheScrollDistanceOverTheSpeed")]
        [TestCase(11, 1, 0.8096f, TestName = "PodiumApproach_Rank12_LastsTheScrollDistanceOverTheSpeed")]
        [TestCase(19, 0, 1.5264f, TestName = "PodiumApproach_Rank20_LastsTheScrollDistanceOverTheSpeed")]
        [TestCase(24, 0, 1.9744f, TestName = "PodiumApproach_Rank25_LastsTheScrollDistanceOverTheSpeed")]
        public void PodiumApproach_LastsTheScrollDistanceOverTheSpeed(int startRank, int targetRank, float referenceDuration)
        {
            BoardModel model = NewModel(startRank, targetRank, ApproachSettings());
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Prepare();
            Assert.IsTrue(model.HasPodiumApproach, "Tiền đề hỏng: lên bục từ list mà không có cú tiếp cận.");
            float startScroll = ReferenceStartScroll(timeline.Plan.StartSlot);
            model.SetPodiumApproachStartScroll(startScroll);
            timeline.Start();

            float expectedDuration = startScroll / ReferenceScrollSpeed;
            Assert.AreEqual(referenceDuration, expectedDuration, DurationTolerance,
                            "Quãng cuộn của bố cục tham chiếu đã lệch (231,95 + (hạng − 4) × 224).");
            AssertApproachLasts(timeline, model, expectedDuration, "hạng " + (startRank + 1));
        }

        /// <summary>
        /// Nhịp tick 0,18 s tính trên thời lượng tiếp cận: k × 0,18 &lt; max(0,18, D − 0,18) ⇒ 1 / 1 / 4 / 8 / 10 nhịp cho hạng 4 /
        /// 5 / 12 / 20 / 25, tất cả trước PodiumTakeover.
        /// </summary>
        [TestCase(3, 0, 1)]
        [TestCase(4, 1, 1)]
        [TestCase(11, 1, 4)]
        [TestCase(19, 0, 8)]
        [TestCase(24, 0, 10)]
        public void PodiumApproach_TicksFollowTheApproachDuration(int startRank, int targetRank, int expectedTicks)
        {
            BoardModel model = NewModel(startRank, targetRank, ApproachSettings());
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);
            timeline.Prepare();
            model.SetPodiumApproachStartScroll(ReferenceStartScroll(timeline.Plan.StartSlot));
            timeline.Start();

            RunUntil(timeline, model, () => timeline.Phase == RevealPhase.PodiumHold);

            Assert.AreEqual(expectedTicks, listener.CountOf(LeaderboardBeat.Pass), "Số nhịp tick của cú tiếp cận.");
            Assert.Less(listener.Beats.LastIndexOf(LeaderboardBeat.Pass), listener.Beats.IndexOf(LeaderboardBeat.PodiumTakeover));
        }

        /// <summary>
        /// Bắt đầu ĐÚNG ô ranh giới (hạng 4): vẫn có cú tiếp cận (camera phải cuộn một quãng thật), với đúng một nhịp tick — trước
        /// 0.6.0 (và khi cờ tắt) màn này tới cổng ngay sau Lift, không pha Climb, không nhịp nào.
        /// </summary>
        [Test]
        public void PodiumApproach_BoundaryStart_StillClimbs_WithOneTick()
        {
            BoardModel model = NewModel(3, 1, ApproachSettings());
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);
            timeline.Prepare();
            Assert.AreEqual(TopRanks, timeline.Plan.StartSlot, 1e-4f, "Tiền đề hỏng: phải bắt đầu ở ô ranh giới.");
            model.SetPodiumApproachStartScroll(ReferenceStartScroll(timeline.Plan.StartSlot));
            timeline.Start();

            var phases = new List<RevealPhase>();
            for (int tick = 0; tick < MaximumTicks && timeline.Phase != RevealPhase.PodiumHold; tick++)
            {
                Step(timeline, model, FrameDeltaTime);
                phases.Add(timeline.Phase);
            }

            CollectionAssert.Contains(phases, RevealPhase.Climb, "Bắt đầu ở ranh giới mà không có cú tiếp cận.");
            Assert.AreEqual(1, listener.CountOf(LeaderboardBeat.Pass));
            CollectionAssert.AreEqual(new[] { LeaderboardBeat.RevealStarted, LeaderboardBeat.Lift, LeaderboardBeat.Pass,
                                              LeaderboardBeat.PodiumTakeover }, listener.Beats);
        }

        /// <summary>Quãng cuộn 0 (màn rất cao): cú tiếp cận xong ngay trong tick nó bắt đầu, vẫn đúng một nhịp tick.</summary>
        [Test]
        public void PodiumApproach_ZeroScrollDistance_IsInstant_WithExactlyOneTick()
        {
            BoardModel model = NewModel(3, 0, ApproachSettings());
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);
            model.SetPodiumApproachStartScroll(0f);
            timeline.Start();

            AdvanceToApproachStart(timeline, model);

            Assert.AreEqual(RevealPhase.PodiumHold, timeline.Phase, "Quãng cuộn 0 thì tới cổng ngay trong tick hết Lift.");
            Assert.AreEqual(1, listener.CountOf(LeaderboardBeat.Pass), "D ≤ 0 vẫn phải có đúng một nhịp tick.");
            Assert.AreEqual(1, listener.CountOf(LeaderboardBeat.PodiumTakeover));
        }

        /// <summary>
        /// Slot của row mình và tiến độ camera đọc từ model là CÙNG một giá trị ở mọi tick: Slot = lerp(ô xuất phát, ranh giới,
        /// <see cref="BoardModel.PodiumApproachProgress"/>). Tiến độ tăng đều từ 0 lên 1 theo đường cuộn.
        /// </summary>
        [Test]
        public void PodiumApproach_RowSlotAndCameraProgress_AreTheSameEasedValue()
        {
            BoardModel model = NewModel(11, 1, ApproachSettings());
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Prepare();
            model.SetPodiumApproachStartScroll(ReferenceStartScroll(timeline.Plan.StartSlot));
            timeline.Start();
            float startSlot = timeline.Plan.StartSlot;

            RunUntil(timeline, model, () => timeline.Phase == RevealPhase.Climb);
            float previous = -1f;
            int climbTicks = 0;
            while (timeline.Phase == RevealPhase.Climb)
            {
                float progress = model.PodiumApproachProgress;
                Assert.AreEqual(startSlot + (TopRanks - startSlot) * progress, model.LocalRow.Slot, 1e-4f,
                                "Tick " + climbTicks + ": Slot của row mình không đi theo tiến độ camera đọc.");
                Assert.GreaterOrEqual(progress, previous, "Tiến độ cú tiếp cận đi lùi.");
                previous = progress;
                climbTicks++;
                Step(timeline, model, FrameDeltaTime);
            }

            Assert.Greater(climbTicks, 30, "Tiền đề hỏng: cú tiếp cận 0,81 s phải dài ~48 tick.");
            Assert.AreEqual(RevealPhase.PodiumHold, timeline.Phase);
            Assert.AreEqual(1f, model.PodiumApproachProgress, 1e-6f);
            Assert.AreEqual(TopRanks, model.LocalRow.Slot, 1e-4f);
        }

        /// <summary>Không list nào báo quãng cuộn (model tự lái): thời lượng rơi về công thức leo thường theo số người vượt.</summary>
        [Test]
        public void PodiumApproach_WithoutAScrollReport_FallsBackToTheClimbFormula()
        {
            MotionSettings settings = ApproachSettings();
            BoardModel model = NewModel(11, 1, settings);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Prepare();
            Assert.IsTrue(float.IsNaN(model.PodiumApproachStartScroll), "Chưa ai báo quãng cuộn.");
            int listPassCount = timeline.Plan.LocalIndex + timeline.Plan.AnimatedCount - model.HiddenLeadingSlots;
            timeline.Start();

            AssertApproachLasts(timeline, model, RankUpPlanner.ClimbDuration(listPassCount, settings), "không có list");
        }

        /// <summary>Đổi chỗ trên bục (#3 → #2): không có đoạn list nào để cuộn — không có cú tiếp cận, chờ ngay sau Lift như cũ.</summary>
        [TestCase(2, 1)]
        [TestCase(1, 0)]
        public void PodiumApproach_NotForSwapsOnThePodium(int startRank, int targetRank)
        {
            BoardModel model = NewModel(startRank, targetRank, ApproachSettings());
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);
            timeline.Start();

            Assert.IsFalse(model.HasPodiumApproach);
            RunUntil(timeline, model, () => timeline.Phase == RevealPhase.PodiumHold);
            CollectionAssert.AreEqual(new[] { LeaderboardBeat.RevealStarted, LeaderboardBeat.Lift, LeaderboardBeat.PodiumTakeover },
                                      listener.Beats);
        }

        /// <summary>Cờ tắt (tốc độ 0): không có cú tiếp cận; bắt đầu ở ranh giới thì tới cổng ngay sau Lift như 0.5.0.</summary>
        [Test]
        public void PodiumApproach_FlagOff_HasNoApproach()
        {
            BoardModel model = NewModel(3, 1, ApproachSettings(scrollSpeed: 0f));
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);
            timeline.Start();

            Assert.IsFalse(model.HasPodiumApproach);
            Assert.IsTrue(timeline.TakesPodium);
            RunUntil(timeline, model, () => timeline.Phase == RevealPhase.PodiumHold);
            CollectionAssert.AreEqual(new[] { LeaderboardBeat.RevealStarted, LeaderboardBeat.Lift, LeaderboardBeat.PodiumTakeover },
                                      listener.Beats);
        }

        /// <summary>Bỏ qua hay đóng giữa cú tiếp cận: camera về nhà (tiến độ 1) — không kẹt ở giữa quãng cuộn.</summary>
        [TestCase(true, TestName = "PodiumApproach_SkipMidway_SendsTheCameraHome")]
        [TestCase(false, TestName = "PodiumApproach_ForceFinishMidway_SendsTheCameraHome")]
        public void PodiumApproach_InterruptedMidway_SendsTheCameraHome(bool skips)
        {
            BoardModel model = NewModel(19, 0, ApproachSettings());
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Prepare();
            model.SetPodiumApproachStartScroll(ReferenceStartScroll(timeline.Plan.StartSlot));
            timeline.Start();
            RunUntil(timeline, model, () => timeline.Phase == RevealPhase.Climb);
            for (int tick = 0; tick < 20; tick++) Step(timeline, model, FrameDeltaTime);
            Assert.Less(model.PodiumApproachProgress, 0.9f, "Tiền đề hỏng: phải đang giữa cú tiếp cận.");

            if (skips)
            {
                timeline.RequestSkip();
                Step(timeline, model, FrameDeltaTime);
            }
            else
            {
                timeline.ForceFinish();
            }

            Assert.AreEqual(1f, model.PodiumApproachProgress, 1e-6f);
        }

        // ---------------------------------------------------------------- DeferPodiumApproachPasses

        /// <summary>
        /// #12 → #2: suốt cú tiếp cận và lúc đứng ở cổng, những người bị vượt trong list KHÔNG nhúc nhích và giữ số hạng cũ; đúng
        /// tick host thả cổng, họ cùng ở ô mới (ô cũ + 1) với số hạng thật, không tween, không thêm nhịp Pass — cùng tick row mình
        /// tới ô đích và hai người trên bục đứng vào ô mới.
        /// </summary>
        [Test]
        public void DeferPodiumApproachPasses_PassedRowsHoldUntilTheRelease_ThenJumpInOneTick()
        {
            BoardModel model = NewModel(11, 1, ApproachSettings());
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);
            timeline.Prepare();
            model.SetPodiumApproachStartScroll(ReferenceStartScroll(timeline.Plan.StartSlot));
            timeline.Start();

            RankUpPlan plan = timeline.Plan;
            int listPassCount = plan.LocalIndex + plan.AnimatedCount - model.HiddenLeadingSlots;
            Assert.Greater(listPassCount, 3, "Tiền đề hỏng: đoạn trong list phải vượt nhiều người.");
            var listPassed = new List<RowState>();
            var startSlots = new List<float>();
            var startRanks = new List<int>();
            for (int index = 0; index < listPassCount; index++)
            {
                listPassed.Add(plan.Passed[index]);
                startSlots.Add(plan.Passed[index].Slot);
                startRanks.Add(plan.Passed[index].DisplayRank);
            }

            int holdTicks = 0;
            for (int tick = 0; tick < MaximumTicks && holdTicks < 10; tick++)
            {
                Step(timeline, model, FrameDeltaTime);
                if (timeline.Phase == RevealPhase.PodiumHold) holdTicks++;
                for (int index = 0; index < listPassed.Count; index++)
                {
                    Assert.AreEqual(startSlots[index], listPassed[index].Slot, 1e-5f, "Tick " + tick + ": người bị vượt đã dời trước lúc thả.");
                    Assert.AreEqual(startRanks[index], listPassed[index].DisplayRank, "Tick " + tick + ": người bị vượt đổi số trước lúc thả.");
                }
            }
            Assert.AreEqual(RevealPhase.PodiumHold, timeline.Phase, "Tiền đề hỏng: phải đang chờ ở cổng bục.");
            int passBeatsBeforeRelease = listener.CountOf(LeaderboardBeat.Pass);
            Assert.Greater(passBeatsBeforeRelease, 0, "Tiền đề hỏng: cú tiếp cận phải có nhịp tick.");

            timeline.ReleasePodiumHold();
            Step(timeline, model, FrameDeltaTime);

            Assert.AreEqual(RevealPhase.Land, timeline.Phase, "Thả cổng mà chưa đáp ngay trong tick đó.");
            Assert.AreEqual(plan.FinalSlot, model.LocalRow.Slot, 1e-5f);
            for (int index = 0; index < listPassed.Count; index++)
            {
                RowState row = listPassed[index];
                Assert.AreEqual(startSlots[index] + 1f, row.Slot, 1e-5f, "Người bị vượt phải ở ô mới ngay tick thả cổng.");
                Assert.IsFalse(row.IsSlotTweening, "PodiumPassSlideDuration = 0 thì đặt thẳng, không tween.");
                Assert.AreEqual(row.Entry.Rank, row.DisplayRank, "Người bị vượt phải mang số hạng thật ngay tick thả cổng.");
            }
            for (int index = listPassCount; index < plan.AnimatedCount; index++)
            {
                RowState podiumRow = plan.Passed[index];
                Assert.AreEqual(model.IndexOf(podiumRow), podiumRow.Slot, 1e-5f, "Người trên bục bị vượt phải ở ô cuối cùng tick.");
            }
            Assert.AreEqual(passBeatsBeforeRelease, listener.CountOf(LeaderboardBeat.Pass), "Tick thả cổng không được có nhịp Pass.");

            RunUntil(timeline, model, () => timeline.IsFinished);
            model.FinishAllTweens();
            RankUpPlannerInvariantTests.AssertRowsSettled(model, "12→2");
        }

        // ---------------------------------------------------------------- PodiumApproachStopOffsetRows

        /// <summary>
        /// Chỗ dừng của game tham chiếu: đích cú tiếp cận tính bằng vùng bục 638 trên ảnh thật 649 ⇒ thẻ nổi dừng 750 dưới mép trên
        /// khung nhìn, CAO hơn tâm ô hạng 4 (758) đúng 8 px = −8 / 224 bước. Row mình đi thẳng tới đó theo cùng tiến độ camera, đứng
        /// chờ ở đó suốt cổng bục, rồi thả cổng thì tới ô đích như cũ.
        /// </summary>
        [TestCase(3, 0)]
        [TestCase(11, 1)]
        public void PodiumApproachStopOffset_RowStopsAboveTheBoundary_AndWaitsThere(int startRank, int targetRank)
        {
            const float stopOffset = -8f / 224f;
            MotionSettings settings = ApproachSettings();
            settings.PodiumApproachStopOffsetRows = stopOffset;
            BoardModel model = NewModel(startRank, targetRank, settings);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Prepare();
            model.SetPodiumApproachStartScroll(ReferenceStartScroll(timeline.Plan.StartSlot));
            timeline.Start();
            float startSlot = timeline.Plan.StartSlot;
            float stopSlot = TopRanks + stopOffset;

            RunUntil(timeline, model, () => timeline.Phase == RevealPhase.Climb);
            while (timeline.Phase == RevealPhase.Climb)
            {
                Assert.AreEqual(startSlot + (stopSlot - startSlot) * model.PodiumApproachProgress, model.LocalRow.Slot, 1e-4f,
                                "Slot của row mình phải đi theo tiến độ camera tới chỗ dừng lệch.");
                Step(timeline, model, FrameDeltaTime);
            }
            Assert.AreEqual(RevealPhase.PodiumHold, timeline.Phase);
            Assert.AreEqual(stopSlot, model.LocalRow.Slot, 1e-5f, "Hết cú tiếp cận: dừng lệch khỏi ô ranh giới.");
            for (int tick = 0; tick < 20; tick++) Step(timeline, model, FrameDeltaTime);
            Assert.AreEqual(stopSlot, model.LocalRow.Slot, 1e-5f, "Đứng chờ ở cổng đúng chỗ dừng lệch.");

            timeline.ReleasePodiumHold();
            Step(timeline, model, FrameDeltaTime);
            Assert.AreEqual(timeline.Plan.FinalSlot, model.LocalRow.Slot, 1e-5f, "Thả cổng thì tới ô đích như cũ.");
            RunUntil(timeline, model, () => timeline.IsFinished);
            model.FinishAllTweens();
            RankUpPlannerInvariantTests.AssertRowsSettled(model, (startRank + 1) + "→" + (targetRank + 1));
        }

        /// <summary>Không có cú tiếp cận (đổi chỗ trên bục, hay tốc độ 0): chỗ dừng lệch KHÔNG áp — row đứng đúng ô như 0.5.0.</summary>
        [TestCase(2, 1, ReferenceScrollSpeed)]
        [TestCase(3, 1, 0f)]
        public void PodiumApproachStopOffset_WithoutAnApproach_IsIgnored(int startRank, int targetRank, float scrollSpeed)
        {
            MotionSettings settings = ApproachSettings(scrollSpeed: scrollSpeed);
            settings.PodiumApproachStopOffsetRows = -8f / 224f;
            BoardModel model = NewModel(startRank, targetRank, settings);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Start();

            Assert.IsFalse(model.HasPodiumApproach);
            RunUntil(timeline, model, () => timeline.Phase == RevealPhase.PodiumHold);
            Assert.AreEqual(Math.Min(startRank, TopRanks), model.LocalRow.Slot, 1e-5f);
        }

        // ---------------------------------------------------------------- PodiumApproachShortfallRows

        /// <summary>
        /// Dừng hụt của game tham chiếu (vùng bục model 638 trên ảnh thật 649 ⇒ hụt 11 = 11 / 224 bước, chỉ khi vùng bục đã khuất lúc
        /// bắt đầu): list báo chỗ cuộn cuối 11 ⇒ thời lượng = (quãng cuộn − 11) / 2500 (12 → 2: 0,8052 s), camera dừng ở 11, row dừng
        /// ở ô ranh giới + (−8 + 11) / 224 bước — trên màn (tâm ô − camera) đúng chỗ dừng khi không hụt.
        /// </summary>
        [Test]
        public void PodiumApproachShortfall_StopsShortOfTheTop_TheRowStillStopsAtTheSameScreenPoint()
        {
            const float stopOffset = -8f / 224f;
            const float shortfallRows = 11f / 224f;
            MotionSettings settings = ApproachSettings();
            settings.PodiumApproachStopOffsetRows = stopOffset;
            settings.PodiumApproachShortfallRows = shortfallRows;
            BoardModel model = NewModel(11, 1, settings);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Prepare();
            float startScroll = ReferenceStartScroll(timeline.Plan.StartSlot);
            float endScroll = shortfallRows * ReferenceLayout.Stride;
            model.SetPodiumApproachScrollRange(startScroll, endScroll);
            Assert.IsTrue(model.PodiumApproachStopsShort);
            Assert.AreEqual(11f, model.PodiumApproachEndScroll, 1e-4f);
            timeline.Start();

            float expectedDuration = (startScroll - endScroll) / ReferenceScrollSpeed;
            Assert.AreEqual(0.8052f, expectedDuration, DurationTolerance, "(2023,95 − 11) / 2500.");
            AssertApproachLasts(timeline, model, expectedDuration, "12 → 2 dừng hụt");

            float stopSlot = TopRanks + stopOffset + shortfallRows;
            Assert.AreEqual(stopSlot, model.LocalRow.Slot, 1e-5f, "Row dời theo quãng hụt.");
            float screenStop = ReferenceLayout.SlotToCenter(model.LocalRow.Slot) - model.PodiumApproachEndScroll;
            Assert.AreEqual(ReferenceLayout.SlotToCenter(TopRanks + stopOffset), screenStop, 1e-3f,
                            "Trên màn row dừng đúng chỗ của cú tiếp cận không hụt.");
        }

        /// <summary>
        /// List báo chỗ cuộn cuối 0 (vùng bục còn thấy lúc bắt đầu — hạng xuất phát 5 trên 1920) thì quãng hụt KHÔNG áp dù đã bật:
        /// thời lượng = quãng cuộn / 2500, row dừng ở chỗ dừng lệch thường.
        /// </summary>
        [Test]
        public void PodiumApproachShortfall_WhenTheListStopsAtTheTop_IsIgnored()
        {
            const float stopOffset = -8f / 224f;
            MotionSettings settings = ApproachSettings();
            settings.PodiumApproachStopOffsetRows = stopOffset;
            settings.PodiumApproachShortfallRows = 11f / 224f;
            BoardModel model = NewModel(4, 1, settings);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Prepare();
            float startScroll = ReferenceStartScroll(timeline.Plan.StartSlot);
            model.SetPodiumApproachStartScroll(startScroll);
            Assert.IsFalse(model.PodiumApproachStopsShort);
            timeline.Start();

            AssertApproachLasts(timeline, model, startScroll / ReferenceScrollSpeed, "5 → 2");
            Assert.AreEqual(TopRanks + stopOffset, model.LocalRow.Slot, 1e-5f);
        }

        /// <summary>Hai đầu của cú tiếp cận kẹp đúng: âm về 0, chỗ cuối không vượt chỗ đầu; báo chỉ chỗ đầu thì chỗ cuối là 0.</summary>
        [Test]
        public void PodiumApproachScrollRange_IsClamped()
        {
            BoardModel model = NewModel(11, 1, ApproachSettings());
            model.SetPodiumApproachScrollRange(-5f, 3f);
            Assert.AreEqual(0f, model.PodiumApproachStartScroll);
            Assert.AreEqual(0f, model.PodiumApproachEndScroll);
            model.SetPodiumApproachScrollRange(100f, 150f);
            Assert.AreEqual(100f, model.PodiumApproachEndScroll, "Chỗ cuối không vượt chỗ đầu.");
            model.SetPodiumApproachStartScroll(200f);
            Assert.AreEqual(0f, model.PodiumApproachEndScroll, "Báo chỉ chỗ đầu = về đỉnh list như 0.6.0.");
            Assert.IsFalse(model.PodiumApproachStopsShort);
        }

        /// <summary>Đối chứng: cờ tắt thì người bị vượt nhường chỗ ngay trong cú tiếp cận (hành vi 0.5.0).</summary>
        [Test]
        public void DeferPodiumApproachPasses_FlagOff_PassedRowsMakeRoomDuringTheApproach()
        {
            BoardModel model = NewModel(11, 1, ApproachSettings(defersApproachPasses: false));
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Prepare();
            model.SetPodiumApproachStartScroll(ReferenceStartScroll(timeline.Plan.StartSlot));
            timeline.Start();
            RowState firstPassed = timeline.Plan.Passed[0];
            float firstPassedStartSlot = firstPassed.Slot;

            RunUntil(timeline, model, () => timeline.Phase == RevealPhase.PodiumHold);

            Assert.AreEqual(firstPassedStartSlot + 1f, firstPassed.TargetSlot, 1e-5f, "Cờ tắt: người bị vượt phải đã nhường ô.");
        }

        /// <summary>Bỏ qua ở mọi tick của cú tiếp cận có người đứng chờ: luôn về trạng thái cuối, không ai kẹt ở ô cũ / số cũ.</summary>
        [Test]
        public void DeferPodiumApproachPasses_SkipAtEveryTick_SettlesEveryRow()
        {
            for (int skipTick = 0; skipTick < 120; skipTick += 3)
            {
                BoardModel model = NewModel(11, 1, ApproachSettings());
                var timeline = new RevealTimeline(model, new RecordingRevealListener());
                timeline.Prepare();
                model.SetPodiumApproachStartScroll(ReferenceStartScroll(timeline.Plan.StartSlot));
                timeline.Start();
                for (int tick = 0; tick < MaximumTicks && !timeline.IsFinished; tick++)
                {
                    if (tick == skipTick) timeline.RequestSkip();
                    if (timeline.Phase == RevealPhase.PodiumHold) timeline.ReleasePodiumHold();
                    Step(timeline, model, FrameDeltaTime);
                }
                Assert.IsTrue(timeline.IsFinished, "skip@" + skipTick);
                model.FinishAllTweens();
                RankUpPlannerInvariantTests.AssertRowsSettled(model, "skip@" + skipTick);
            }
        }

        // ---------------------------------------------------------------- HostPresentedRowSkipsQuietPulse

        /// <summary>
        /// Có điểm mà vẫn đứng trên bục (#2 → #2, ScoreImproved): màn diễn XONG ngay trong tick hết Intro — không tick nào kết
        /// thúc ở CountScore / Bob. Cờ tắt thì nhịp nhẹ vô hình 0,2 + 0,22 s giữ màn diễn lại.
        /// </summary>
        [TestCase(true, TestName = "SkipsQuietPulse_ScoreImprovedOnThePodium_FinishesInTheIntroTick")]
        [TestCase(false, TestName = "SkipsQuietPulse_FlagOff_ScoreImprovedOnThePodium_KeepsTheInvisiblePulse")]
        public void HostPresentedRowSkipsQuietPulse_ScoreImprovedOnThePodium(bool skipsPulse)
        {
            BoardScene scene = LeaderboardScenario.Create(maximumAnimatedPasses: 60).LoadScoreImprovedScene(1);
            Assert.AreEqual(RankChangeKind.ScoreImproved, scene.Change.Kind, "Tiền đề hỏng: phải là ScoreImproved.");
            var model = new BoardModel(scene, ApproachSettings(skipsHostPresentedPulse: skipsPulse));
            Assert.AreEqual(0f, model.ListPresence(model.LocalRow), "Tiền đề hỏng: row mình phải đang trên bục.");

            int ticksAfterIntro = CountTicksAfterIntro(model);

            if (skipsPulse) Assert.AreEqual(0, ticksAfterIntro, "Row trên bục: màn diễn phải xong ngay trong tick hết Intro.");
            else Assert.AreEqual((0.2f + 0.22f) / FrameDeltaTime, ticksAfterIntro, 1.5f, "Cờ tắt: nhịp nhẹ 0,42 s như 0.5.0.");
        }

        /// <summary>Row mình trong LIST (có điểm, cùng hạng): cờ không đụng tới — nhịp nhẹ vẫn đủ 0,42 s.</summary>
        [Test]
        public void HostPresentedRowSkipsQuietPulse_RowInTheList_KeepsThePulse()
        {
            BoardScene scene = LeaderboardScenario.Create(maximumAnimatedPasses: 60).LoadScoreImprovedScene(20);
            var model = new BoardModel(scene, ApproachSettings());
            Assert.AreEqual(1f, model.ListPresence(model.LocalRow), "Tiền đề hỏng: row mình phải nằm trong list.");

            Assert.AreEqual((0.2f + 0.22f) / FrameDeltaTime, CountTicksAfterIntro(model), 1.5f);
        }

        /// <summary>Tụt trong bục (#1 → #3): cờ bật thì cũng xong ngay trong tick hết Intro — host quyết định bằng cú phồng của nó.</summary>
        [TestCase(true, 0)]
        [TestCase(false, 25)]
        public void HostPresentedRowSkipsQuietPulse_RankDownOnThePodium(bool skipsPulse, int expectedTicks)
        {
            RankChange change = RankChange.Create(RankChangeKind.RankDown, 0, 2, 5000, 5000);
            BoardScene scene = RevealTimelinePodiumTests.SyntheticScene(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, 2, change);
            var model = new BoardModel(scene, ApproachSettings(skipsHostPresentedPulse: skipsPulse));

            Assert.AreEqual(expectedTicks, CountTicksAfterIntro(model), 1.5f);
        }

        /// <summary>Số tick còn chạy sau tick kết thúc Intro (0 = xong ngay trong tick đó).</summary>
        private static int CountTicksAfterIntro(BoardModel model)
        {
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Start();
            RunUntil(timeline, model, () => timeline.Phase != RevealPhase.Intro);
            int ticks = 0;
            while (!timeline.IsFinished && ticks < MaximumTicks)
            {
                Step(timeline, model, FrameDeltaTime);
                ticks++;
            }
            Assert.IsTrue(timeline.IsFinished);
            return ticks;
        }

        // ---------------------------------------------------------------- Cú lên bục: số hạng, hào quang, dòng mũi tên (P5)

        /// <summary>
        /// Từ tick nhịp Lift trở đi row mình mang ĐÚNG số hạng cuối (không cuộn số, không pha Spin) — game tham chiếu ghi số hạng
        /// mới ngay lúc nhấc rồi mới cuộn (<c>RankFlipsBeforeRankMove</c> của host).
        /// </summary>
        [Test]
        public void PodiumPromotion_ShowsTheFinalRankFromTheLift_WithoutSpin()
        {
            BoardModel model = NewModel(19, 0, ApproachSettings());
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);
            timeline.Prepare();
            model.SetPodiumApproachStartScroll(ReferenceStartScroll(timeline.Plan.StartSlot));
            timeline.Start();

            int checkedTicks = 0;
            for (int tick = 0; tick < MaximumTicks && !timeline.IsFinished; tick++)
            {
                if (timeline.Phase == RevealPhase.PodiumHold) timeline.ReleasePodiumHold();
                Step(timeline, model, FrameDeltaTime);
                Assert.AreNotEqual(RevealPhase.Spin, timeline.Phase, "Lên bục từ list không được quay số.");
                if (listener.CountOf(LeaderboardBeat.Lift) == 0) continue;
                Assert.AreEqual(model.Scene.Change.ToRank, model.LocalRow.DisplayRank, "Tick " + tick + ": số hạng chưa là hạng cuối.");
                checkedTicks++;
            }
            Assert.Greater(checkedTicks, 60, "Tiền đề hỏng: phải kiểm suốt cú tiếp cận 1,5 s.");
        }

        /// <summary>
        /// Hào quang (glow theo đồng hồ riêng) và dòng mũi tên của row mình còn chạy ĐÚNG lúc nhịp PodiumTakeover — package không
        /// tắt dần gì trên đường này; host giành thanh và cắt chúng ngay ở nhịp đó.
        /// </summary>
        [Test]
        public void PodiumPromotion_HaloAndRankUpStreamRunUntilTheTakeover()
        {
            BoardModel model = NewModel(11, 1, ApproachSettings());
            var listener = new TakeoverSnapshotListener(model);
            var timeline = new RevealTimeline(model, listener);
            timeline.Prepare();
            model.SetPodiumApproachStartScroll(ReferenceStartScroll(timeline.Plan.StartSlot));
            timeline.Start();

            RunUntil(timeline, model, () => listener.IsCaptured);

            Assert.IsTrue(listener.IsGlowEnvelopeActive, "Hào quang đã tắt trước nhịp PodiumTakeover.");
            Assert.AreEqual(1f, listener.GlowBoost, 1e-4f, "Hào quang phải sáng đầy lúc host nhận quyền.");
            Assert.IsTrue(listener.HasRankUpStream, "Dòng mũi tên chưa bao giờ chạy.");
            Assert.IsTrue(listener.IsRankUpStreamRunning, "Dòng mũi tên đã dừng trước nhịp PodiumTakeover.");
        }

        /// <summary>Chụp trạng thái row mình đúng lúc nhịp PodiumTakeover phát.</summary>
        private sealed class TakeoverSnapshotListener : IRevealListener
        {
            private readonly BoardModel _model;

            public TakeoverSnapshotListener(BoardModel model)
            {
                _model = model;
            }

            public bool IsCaptured { get; private set; }
            public bool IsGlowEnvelopeActive { get; private set; }
            public float GlowBoost { get; private set; }
            public bool HasRankUpStream { get; private set; }
            public bool IsRankUpStreamRunning { get; private set; }

            public void OnBeat(LeaderboardBeat beat, in LeaderboardBeatContext context)
            {
                if (beat != LeaderboardBeat.PodiumTakeover || IsCaptured) return;
                RowState local = _model.LocalRow;
                IsCaptured = true;
                IsGlowEnvelopeActive = local.IsGlowEnvelopeActive;
                GlowBoost = local.GlowBoost;
                HasRankUpStream = local.HasRankUpStream;
                IsRankUpStreamRunning = double.IsNaN(local.RankUpStreamEndTime);
            }

            public void OnLanded(RankTier tier, RowState localRow)
            {
            }

            public void OnCelebrate(RankTier tier, RowState localRow)
            {
            }

            public void OnTailRevealed(IReadOnlyList<RowState> tail)
            {
            }

            public void OnCameraSnapRequested()
            {
            }
        }

        // ---------------------------------------------------------------- Cờ bật nhưng không lên bục; mặc định; Clone

        /// <summary>
        /// Bật cả ba cờ mà màn lên hạng KHÔNG đáp vào bục (#48 → #37, cả khi bị bỏ qua giữa chừng): từng tick, từng bit trùng khi
        /// tắt chúng — màn leo trong list đã chỉnh từng khung không được đổi.
        /// </summary>
        [TestCase(-1, TestName = "ApproachFlagsOn_NonPodium_48To37_IdenticalToFlagsOff")]
        [TestCase(50, TestName = "ApproachFlagsOn_NonPodium_48To37_Skipped_IdenticalToFlagsOff")]
        public void ApproachFlagsOn_NonPodiumRankUp_IsIdenticalToFlagsOff(int skipAtTick)
        {
            MotionSettings flagsOff = RevealTimelineFlagOffTests.HostLikeSettings();
            flagsOff.HostPresentedTopRanks = TopRanks;
            MotionSettings flagsOn = flagsOff.Clone();
            flagsOn.DeferPodiumApproachPasses = true;
            flagsOn.PodiumApproachScrollSpeed = ReferenceScrollSpeed;
            flagsOn.HostPresentedRowSkipsQuietPulse = true;
            flagsOn.PodiumApproachStopOffsetRows = -8f / 224f;

            RevealTrace reference = RevealTimelineFlagOffTests.RunScenario(47, 36, flagsOff, skipAtTick);
            RevealTrace candidate = RevealTimelineFlagOffTests.RunScenario(47, 36, flagsOn, skipAtTick);

            Assert.IsFalse(candidate.Timeline.TakesPodium, "Tiền đề hỏng: màn diễn này không được lên bục.");
            Assert.IsFalse(candidate.Model.HasPodiumApproach);
            CollectionAssert.AreEqual(reference.Beats, candidate.Beats);
            CollectionAssert.AreEqual(reference.Values, candidate.Values, "Quỹ đạo từng tick khác khi bật các cờ 0.6.0.");
        }

        /// <summary>Mặc định phải là TẮT — asset cũ thiếu field nhận đúng các giá trị này.</summary>
        [Test]
        public void Defaults_AreOff()
        {
            var settings = new MotionSettings();

            Assert.IsFalse(settings.DeferPodiumApproachPasses);
            Assert.AreEqual(0f, settings.PodiumApproachScrollSpeed);
            Assert.AreEqual(0f, settings.PodiumApproachStopOffsetRows);
            Assert.AreEqual(0f, settings.PodiumApproachShortfallRows);
            Assert.IsFalse(settings.HostPresentedRowSkipsQuietPulse);
        }

        /// <summary>Widget chụp settings bằng <c>Clone()</c>: ba field mới phải đi theo.</summary>
        [Test]
        public void Clone_CarriesTheApproachSettings()
        {
            MotionSettings original = ApproachSettings();
            original.PodiumApproachStopOffsetRows = -8f / 224f;
            original.PodiumApproachShortfallRows = 11f / 224f;
            MotionSettings copy = original.Clone();

            Assert.IsTrue(copy.DeferPodiumApproachPasses);
            Assert.AreEqual(ReferenceScrollSpeed, copy.PodiumApproachScrollSpeed);
            Assert.AreEqual(-8f / 224f, copy.PodiumApproachStopOffsetRows);
            Assert.AreEqual(11f / 224f, copy.PodiumApproachShortfallRows);
            Assert.IsTrue(copy.HostPresentedRowSkipsQuietPulse);
        }
    }
}
