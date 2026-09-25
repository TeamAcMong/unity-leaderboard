using System;
using System.Collections.Generic;
using DreamTech.Leaderboard.ViewModel;
using NUnit.Framework;

namespace DreamTech.Leaderboard.Tests
{
    /// <summary>
    /// Các công tắc của "khoảnh khắc lên hạng": <see cref="MotionSettings.RankUpPill"/>, <see cref="MotionSettings.RankUpShine"/>,
    /// <see cref="MotionSettings.LandTwinklesAt"/>, <see cref="MotionSettings.FlashRiseDuration"/> và mốc dòng mũi tên
    /// (<see cref="RowState.RankUpStreamStartTime"/> / <see cref="RowState.RankUpStreamEndTime"/>).
    ///
    /// <para>Như mọi nút opt-in của package: nửa quan trọng là các test "mặc định không đổi gì".</para>
    /// </summary>
    [TestFixture]
    public class RevealTimelineRankUpFeedbackTests
    {
        private const float FrameDeltaTime = 1f / 120f;
        private const int MaximumTicks = 8000;

        /// <summary>Listener ghi lại PHA của timeline ở khoảnh khắc sao được gọi.</summary>
        private sealed class PhaseProbeListener : IRevealListener
        {
            public Func<RevealPhase> PhaseProbe;
            public readonly List<RevealPhase> LandedPhases = new List<RevealPhase>();

            public void OnBeat(LeaderboardBeat beat, in LeaderboardBeatContext context) { }
            public void OnLanded(RankTier tier, RowState localRow) => LandedPhases.Add(PhaseProbe());
            public void OnCelebrate(RankTier tier, RowState localRow) { }
            public void OnTailRevealed(IReadOnlyList<RowState> tail) { }
            public void OnCameraSnapRequested() { }
        }

        private static BoardModel NewRankUpModel(MotionSettings settings)
        {
            LeaderboardScenario scenario = LeaderboardScenario.Create(maximumAnimatedPasses: 18);
            BoardScene scene = scenario.LoadRevealScene(120, 108);
            return new BoardModel(scene, settings);
        }

        private static void RunToEnd(RevealTimeline timeline, BoardModel model, Action<RevealTimeline> perTick = null)
        {
            timeline.Start();
            for (int tick = 0; tick < MaximumTicks && !timeline.IsFinished; tick++)
            {
                timeline.Tick(FrameDeltaTime);
                model.Advance(FrameDeltaTime);
                perTick?.Invoke(timeline);
            }
            Assert.IsTrue(timeline.IsFinished, "Tiền đề hỏng: màn diễn không kết thúc.");
        }

        [Test]
        public void Defaults_RankUp_StartsPillShineAndTwinklesOnce()
        {
            BoardModel model = NewRankUpModel(new MotionSettings());
            var listener = new RecordingRevealListener();
            RunToEnd(new RevealTimeline(model, listener), model);

            Assert.IsTrue(model.LocalRow.PillTiming.IsStarted, "Mặc định mà pill ▲N không còn bật.");
            Assert.AreEqual(PillContent.RankUp, model.LocalRow.PillContent);
            Assert.IsTrue(model.LocalRow.ShineTiming.IsStarted, "Mặc định mà shine không còn quét.");
            Assert.AreEqual(1, listener.LandedCount);
        }

        [Test]
        public void RankUpPillOff_NeverStartsPill()
        {
            BoardModel model = NewRankUpModel(new MotionSettings { RankUpPill = false });
            RunToEnd(new RevealTimeline(model, new RecordingRevealListener()), model);
            Assert.IsFalse(model.LocalRow.PillTiming.IsStarted);
        }

        [Test]
        public void RankUpShineOff_NeverStartsShine()
        {
            BoardModel model = NewRankUpModel(new MotionSettings { RankUpShine = false });
            RunToEnd(new RevealTimeline(model, new RecordingRevealListener()), model);
            Assert.IsFalse(model.LocalRow.ShineTiming.IsStarted);
        }

        /// <summary>Mặc định + lật sớm: sao bung ở cú lật (trước pha đáp) — hành vi cũ.</summary>
        [Test]
        public void DefaultTwinkles_WithEarlyFlip_FireBeforeLand()
        {
            BoardModel model = NewRankUpModel(new MotionSettings { RankFlipsBeforeRankMove = true });
            var listener = new PhaseProbeListener();
            var timeline = new RevealTimeline(model, listener);
            listener.PhaseProbe = () => timeline.Phase;
            RunToEnd(timeline, model);

            Assert.AreEqual(1, listener.LandedPhases.Count);
            Assert.AreNotEqual(RevealPhase.Land, listener.LandedPhases[0]);
        }

        /// <summary>LandTwinklesAt ≥ 0: sao bung TRONG pha đáp, đúng một lần — kể cả khi lật sớm.</summary>
        [Test]
        public void LandTwinklesAt_FiresOnceInsideLand()
        {
            BoardModel model = NewRankUpModel(new MotionSettings { RankFlipsBeforeRankMove = true, LandTwinklesAt = 0.5f });
            var listener = new PhaseProbeListener();
            var timeline = new RevealTimeline(model, listener);
            listener.PhaseProbe = () => timeline.Phase;
            RunToEnd(timeline, model);

            Assert.AreEqual(1, listener.LandedPhases.Count, "Sao phải bung đúng một lần.");
            Assert.AreEqual(RevealPhase.Land, listener.LandedPhases[0]);
        }

        /// <summary>Mặc định FlashRiseDuration = 0: flash bật ở đỉnh ngay frame đầu, như cũ.</summary>
        [Test]
        public void DefaultFlash_JumpsStraightToPeak()
        {
            var settings = new MotionSettings { LandFlashAt = 0f, LandFlashAlpha = 0.3f };
            BoardModel model = NewRankUpModel(settings);
            float firstFlash = 0f;
            RunToEnd(new RevealTimeline(model, new RecordingRevealListener()), model, _ =>
            {
                if (firstFlash <= 0f && model.LocalRow.Flash > 0f) firstFlash = model.LocalRow.Flash;
            });
            Assert.Greater(firstFlash, 0.29f, "Mặc định mà flash không còn bật tức thì.");
        }

        /// <summary>FlashRiseDuration &gt; 0: flash LÊN dần tới đúng đỉnh trong đúng khoảng đó, rồi mới tắt.</summary>
        [Test]
        public void FlashRiseDuration_RisesToPeakOverRiseTime()
        {
            var settings = new MotionSettings
            {
                LandFlashAt = 0f,
                LandFlashAlpha = 0.22f,
                FlashDuration = 0.47f,
                FlashRiseDuration = 0.12f,
                LandDuration = 0.26f,
            };
            BoardModel model = NewRankUpModel(settings);
            var samples = new List<float>();
            RunToEnd(new RevealTimeline(model, new RecordingRevealListener()), model, _ => samples.Add(model.LocalRow.Flash));
            for (int extra = 0; extra < 120; extra++)
            {
                model.Advance(FrameDeltaTime);
                samples.Add(model.LocalRow.Flash);
            }

            int first = samples.FindIndex(value => value > 0f);
            Assert.GreaterOrEqual(first, 0, "Flash không nổ.");
            Assert.Less(samples[first], 0.22f * 0.5f, "Flash vẫn bật tức thì — sườn lên không có tác dụng.");

            float peak = 0f;
            int peakAt = first;
            for (int index = first; index < samples.Count; index++)
            {
                if (samples[index] > peak) { peak = samples[index]; peakAt = index; }
            }
            Assert.AreEqual(0.22f, peak, 0.005f, "Đỉnh flash sai.");
            Assert.AreEqual(0.12f, (peakAt - first + 1) * FrameDeltaTime, 0.02f, "Sườn lên sai độ dài.");
            Assert.Less(samples[samples.Count - 1], peak, "Flash phải tắt dần sau đỉnh.");
        }

        /// <summary>Dòng mũi tên: bắt đầu ở đầu pha nhấc, thôi ở đầu pha đáp.</summary>
        [Test]
        public void RankUpStream_StartsAtLift_EndsAtLand()
        {
            BoardModel model = NewRankUpModel(new MotionSettings());
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            double liftStartedAt = double.NaN, landStartedAt = double.NaN;
            RevealPhase previous = RevealPhase.NotStarted;
            RunToEnd(timeline, model, current =>
            {
                if (current.Phase != previous)
                {
                    if (current.Phase == RevealPhase.Lift) liftStartedAt = model.LocalRow.RankUpStreamStartTime;
                    if (current.Phase == RevealPhase.Land) landStartedAt = model.LocalRow.RankUpStreamEndTime;
                    previous = current.Phase;
                }
                if (current.Phase == RevealPhase.Climb)
                {
                    Assert.IsTrue(double.IsNaN(model.LocalRow.RankUpStreamEndTime), "Dòng mũi tên tắt khi row còn đang leo.");
                }
            });

            Assert.IsTrue(model.LocalRow.HasRankUpStream);
            Assert.IsFalse(double.IsNaN(liftStartedAt), "Vào pha nhấc mà dòng mũi tên chưa bắt đầu.");
            Assert.IsFalse(double.IsNaN(landStartedAt), "Vào pha đáp mà dòng mũi tên chưa dừng.");
            Assert.Greater(landStartedAt, liftStartedAt);
        }

        [Test]
        public void RankUpStream_SkippedMidClimb_StillEnds()
        {
            BoardModel model = NewRankUpModel(new MotionSettings());
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Start();
            for (int tick = 0; tick < MaximumTicks && timeline.Phase != RevealPhase.Climb; tick++)
            {
                timeline.Tick(FrameDeltaTime);
                model.Advance(FrameDeltaTime);
            }
            Assert.AreEqual(RevealPhase.Climb, timeline.Phase, "Tiền đề hỏng: không tới được pha leo.");
            timeline.RequestSkip();
            for (int tick = 0; tick < MaximumTicks && !timeline.IsFinished; tick++)
            {
                timeline.Tick(FrameDeltaTime);
                model.Advance(FrameDeltaTime);
            }
            Assert.IsFalse(double.IsNaN(model.LocalRow.RankUpStreamEndTime), "Bỏ qua mà dòng mũi tên vẫn chạy mãi.");
        }

        /// <summary>Glow ở khoảng 1/3 pha đáp: mặc định đã tắt quá nửa (OutCubic); bật LandGlowFadesLate thì còn gần đầy.</summary>
        [TestCase(false, 0f, 0.5f)]
        [TestCase(true, 0.9f, 1.01f)]
        public void LandGlowFadesLate_HoldsGlowThroughEarlyLand(bool fadesLate, float minimum, float maximum)
        {
            var settings = new MotionSettings { LandDuration = 0.3f, LandGlowFadesLate = fadesLate };
            BoardModel model = NewRankUpModel(settings);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            float landElapsed = 0f;
            float sampled = float.NaN;
            RunToEnd(timeline, model, current =>
            {
                if (current.Phase != RevealPhase.Land) return;
                landElapsed += FrameDeltaTime;
                if (float.IsNaN(sampled) && landElapsed >= 0.1f) sampled = model.LocalRow.GlowBoost;
            });
            Assert.IsFalse(float.IsNaN(sampled), "Tiền đề hỏng: không lấy được mẫu trong pha đáp.");
            Assert.GreaterOrEqual(sampled, minimum);
            Assert.Less(sampled, maximum);
        }

        /// <summary>Sườn tắt: mặc định tắt đều (nửa thời gian còn nửa đỉnh); FlashDecayPower = 2 còn một phần tư.</summary>
        [TestCase(1f, 0.5f)]
        [TestCase(2f, 0.25f)]
        public void FlashDecayPower_ShapesTheFade(float power, float expectedLevelAtHalf)
        {
            var row = new RowState(BoardRow.ForEntry(new LeaderboardEntry("me", "You", 1, 3), true), 0f);
            row.FlashNow(0.4f, 0.4f, 0f, power);
            Assert.AreEqual(1f, row.FlashLevel, 1e-5f);
            var settings = new MotionSettings();
            for (int tick = 0; tick < 24; tick++) row.Advance(1f / 120f, settings);   // 0,2 s = nửa sườn tắt
            Assert.AreEqual(expectedLevelAtHalf, row.FlashLevel, 0.02f);
            Assert.AreEqual(0.4f * expectedLevelAtHalf, row.Flash, 0.01f);
            for (int tick = 0; tick < 60; tick++) row.Advance(1f / 120f, settings);
            Assert.AreEqual(0f, row.Flash, 1e-6f, "Hết thời lượng mà flash chưa tắt hẳn.");
        }

        [Test]
        public void NotRankUp_NoStream()
        {
            LeaderboardScenario scenario = LeaderboardScenario.Create(maximumAnimatedPasses: 18);
            BoardScene scene = scenario.LoadRevealScene(120, 120);
            var model = new BoardModel(scene, new MotionSettings());
            RunToEnd(new RevealTimeline(model, new RecordingRevealListener()), model);
            Assert.IsFalse(model.LocalRow.HasRankUpStream);
        }
    }
}
