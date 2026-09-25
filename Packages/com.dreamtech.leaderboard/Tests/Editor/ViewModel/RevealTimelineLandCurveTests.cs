using System;
using System.Collections.Generic;
using DreamTech.Leaderboard.ViewModel;
using NUnit.Framework;

namespace DreamTech.Leaderboard.Tests
{
    /// <summary>
    /// Các nút opt-in của pha leo/đáp: <see cref="MotionSettings.ClimbEasePower"/>, <see cref="MotionSettings.LandPeakScale"/>
    /// (cú đáp ba đoạn) và <see cref="MotionSettings.LandFlashAt"/> (flash nổ trong pha đáp thay vì ở cú lật).
    ///
    /// <para><b>Nửa quan trọng nhất là các test "mặc định không đổi gì".</b> Package dùng chung cho nhiều game; một nút mới
    /// mà lỡ đổi đường cong mặc định là đổi cảm giác của mọi game đang chạy, không ai được báo.</para>
    /// </summary>
    [TestFixture]
    public class RevealTimelineLandCurveTests
    {
        private const float FrameDeltaTime = 1f / 120f;
        private const int MaximumTicks = 8000;

        private static BoardModel NewRankUpModel(MotionSettings settings)
        {
            LeaderboardScenario scenario = LeaderboardScenario.Create(maximumAnimatedPasses: 18);
            BoardScene scene = scenario.LoadRevealScene(120, 108);
            return new BoardModel(scene, settings);
        }

        /// <summary>Chạy trọn màn diễn, ghi lại cỡ row mình ở từng tick của pha Land.</summary>
        private static List<float> RecordLandScales(MotionSettings settings, out RevealTimeline timeline, out BoardModel model)
        {
            model = NewRankUpModel(settings);
            timeline = new RevealTimeline(model, new RecordingRevealListener());
            var scales = new List<float>();
            timeline.Start();
            for (int tick = 0; tick < MaximumTicks && !timeline.IsFinished; tick++)
            {
                timeline.Tick(FrameDeltaTime);
                model.Advance(FrameDeltaTime);
                if (timeline.Phase == RevealPhase.Land) scales.Add(model.LocalRow.Scale);
            }
            return scales;
        }

        [TestCase(0.1f)]
        [TestCase(0.35f)]
        [TestCase(0.5f)]
        [TestCase(0.8f)]
        public void InOutPower_Three_IsExactlyInOutCubic(float t)
        {
            Assert.AreEqual(Easing.InOutCubic(t), Easing.InOutPower(t, 3f), 1e-5f,
                            "InOutPower(3) phải trùng InOutCubic — nếu không, mặc định của ClimbEasePower đã đổi cú leo cũ.");
        }

        [TestCase(0.25f, 0.125f)]
        [TestCase(0.5f, 0.5f)]
        [TestCase(0.75f, 0.875f)]
        public void InOutPower_Two_IsInOutQuad(float t, float expected)
        {
            Assert.AreEqual(expected, Easing.InOutPower(t, 2f), 1e-5f);
        }

        /// <summary>Mặc định: đường OutBack cũ — từ cỡ lúc đáp đi THẲNG về 1, không bao giờ vọt lên cao hơn cỡ lúc đáp.</summary>
        [Test]
        public void DefaultLand_NeverRisesAboveLandingScale()
        {
            var settings = new MotionSettings { LiftScale = 1.06f };
            List<float> scales = RecordLandScales(settings, out RevealTimeline timeline, out BoardModel model);

            Assert.IsTrue(timeline.IsFinished);
            Assert.Greater(scales.Count, 5, "Tiền đề hỏng: không ghi được pha Land.");
            foreach (float scale in scales)
            {
                Assert.LessOrEqual(scale, 1.06f + 1e-4f, "Mặc định mà pha đáp vọt lên — đường cong cũ đã bị đổi.");
            }
            Assert.AreEqual(1f, model.LocalRow.Scale, 1e-4f);
        }

        /// <summary>
        /// Bật cú đáp ba đoạn: vọt lên ĐỈNH, hụt xuống ĐÁY, về đúng 1 — số lấy từ clip tham chiếu.
        /// </summary>
        [Test]
        public void PunchLand_RisesToPeak_DipsToTrough_SettlesAtOne()
        {
            var settings = new MotionSettings
            {
                LiftScale = 1.06f,
                LandDuration = 0.26f,
                LandPeakScale = 1.134f,
                LandPeakAt = 0.31f,
                LandTroughScale = 0.958f,
                LandTroughAt = 0.79f,
            };
            List<float> scales = RecordLandScales(settings, out RevealTimeline timeline, out BoardModel model);

            Assert.IsTrue(timeline.IsFinished);
            float max = float.MinValue, min = float.MaxValue;
            int maxAt = -1, minAt = -1;
            for (int index = 0; index < scales.Count; index++)
            {
                if (scales[index] > max) { max = scales[index]; maxAt = index; }
                if (scales[index] < min) { min = scales[index]; minAt = index; }
            }

            Assert.AreEqual(1.134f, max, 0.005f, "Đỉnh cú đáp sai.");
            Assert.AreEqual(0.958f, min, 0.005f, "Đáy cú đáp sai.");
            Assert.Less(maxAt, minAt, "Phải vọt lên TRƯỚC rồi mới hụt xuống — ngược lại là OutBack cũ.");
            Assert.AreEqual(1f, model.LocalRow.Scale, 1e-4f, "Kết thúc màn diễn mà row chưa về cỡ 1.");
        }

        /// <summary>
        /// Flash tách khỏi cú lật: lúc số vừa đổi thì row KHÔNG sáng; nó sáng trong pha đáp.
        /// Clip tham chiếu: độ sáng hàng đứng yên ở cú lật, bừng lên lúc đáp.
        /// </summary>
        [Test]
        public void LandFlashAt_MovesFlashFromFlipToLand()
        {
            var settings = new MotionSettings
            {
                RankFlipsBeforeRankMove = true,
                LandFlashAt = 0.8f,
                LandFlashAlpha = 0.3f,
                FlashDuration = 0.35f,
            };
            BoardModel model = NewRankUpModel(settings);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());

            timeline.Start();
            bool flashedBeforeLand = false, flashedInLand = false;
            for (int tick = 0; tick < MaximumTicks && !timeline.IsFinished; tick++)
            {
                timeline.Tick(FrameDeltaTime);
                model.Advance(FrameDeltaTime);
                if (model.LocalRow.Flash > 0f)
                {
                    if (timeline.Phase == RevealPhase.Land || timeline.IsFinished) flashedInLand = true;
                    else flashedBeforeLand = true;
                }
            }

            Assert.IsFalse(flashedBeforeLand, "Flash vẫn nổ ở cú lật / lúc leo — LandFlashAt không có tác dụng.");
            Assert.IsTrue(flashedInLand, "Tách flash khỏi cú lật rồi thì nó phải nổ trong pha đáp.");
        }

        /// <summary>Mặc định LandFlashAt = -1: flash nổ cùng nhịp đáp như cũ (với cờ lật sớm thì là ở cú lật).</summary>
        [Test]
        public void DefaultLandFlashAt_KeepsFlashWithLandingFeedback()
        {
            var settings = new MotionSettings { RankFlipsBeforeRankMove = true };
            BoardModel model = NewRankUpModel(settings);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());

            timeline.Start();
            bool flashedBeforeLand = false;
            for (int tick = 0; tick < MaximumTicks && !timeline.IsFinished; tick++)
            {
                timeline.Tick(FrameDeltaTime);
                model.Advance(FrameDeltaTime);
                if (model.LocalRow.Flash > 0f && timeline.Phase != RevealPhase.Land && !timeline.IsFinished)
                {
                    flashedBeforeLand = true;
                    break;
                }
            }
            Assert.IsTrue(flashedBeforeLand, "Mặc định mà flash không còn đi cùng nhịp đáp nữa.");
        }
    }
}
