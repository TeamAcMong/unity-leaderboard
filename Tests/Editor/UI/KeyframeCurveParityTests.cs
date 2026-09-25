using DreamTech.Leaderboard.UI;
using DreamTech.Leaderboard.ViewModel;
using NUnit.Framework;
using UnityEngine;

namespace DreamTech.Leaderboard.UI.Tests
{
    /// <summary>
    /// <see cref="KeyframeCurve"/> phải đánh giá GIỐNG HỆT <c>AnimationCurve.Evaluate</c> — cả đoạn Hermite lẫn đoạn có trọng
    /// số (Bézier). Các đường cong dưới đây lấy từ một cấu hình chuyển động thật: cú trượt vào, cú
    /// nhấc, cú cuộn (có trọng số) và cú "đóng dấu" (trọng số hai phía, vọt ra ngoài [0, 1]).
    /// </summary>
    public sealed class KeyframeCurveParityTests
    {
        private const float Tolerance = 0.002f;

        private static Keyframe K(float time, float value, float inSlope, float outSlope,
                                  WeightedMode mode = WeightedMode.None, float inWeight = 0f, float outWeight = 0f)
        {
            return new Keyframe(time, value, inSlope, outSlope, inWeight, outWeight) { weightedMode = mode };
        }

        private static AnimationCurve EnterItemCurve() => new AnimationCurve(
            K(0f, 0f, 3.4677072f, 3.4677072f, WeightedMode.None, 0f, 0.07179908f),
            K(0.46425855f, 0.9749657f, 0.4897524f, 0.4897524f, WeightedMode.None, 0.33333334f, 0.51472145f),
            K(1f, 1f, -0.07454029f, -0.07454029f, WeightedMode.None, 0.3536776f, 0f));

        private static AnimationCurve LiftCurve() => new AnimationCurve(
            K(0f, 0f, 1.6092978f, 1.6092978f, WeightedMode.None, 0f, 0.06301823f),
            K(1f, 1f, 0.009571168f, 0.009571168f, WeightedMode.None, 0.17083335f, 0f));

        private static AnimationCurve LoopListMoveCurve() => new AnimationCurve(
            K(0f, 0f, 0f, 0f),
            K(1f, 1f, 0.09481128f, 0.09481128f, WeightedMode.In, 0.47263688f, 0f));

        private static AnimationCurve DropCurve() => new AnimationCurve(
            K(0f, 0f, -9.681747f, -9.681747f, WeightedMode.Both, 0f, 0.29999998f),
            K(0.2487562f, -1.5f, -0.005018755f, -0.005018755f, WeightedMode.In, 0.56f, 0.20133364f),
            K(0.7017883f, 1.3213387f, 10.58718f, 10.58718f, WeightedMode.None, 0.104477696f, 0.1323201f),
            K(1f, 1f, -6.994425f, -6.994425f, WeightedMode.None, 0.1642046f, 0f));

        [Test]
        public void EnterItemCurve_MatchesUnity() => AssertParity(EnterItemCurve());

        [Test]
        public void LiftCurve_MatchesUnity() => AssertParity(LiftCurve());

        [Test]
        public void WeightedLoopListMoveCurve_MatchesUnity() => AssertParity(LoopListMoveCurve());

        [Test]
        public void WeightedDropCurve_MatchesUnity() => AssertParity(DropCurve());

        [Test]
        public void EmptyCurve_IsOff()
        {
            Assert.IsNull(LeaderboardMotionConfig.ToKeyframeCurve(new AnimationCurve()));
            Assert.IsNull(LeaderboardMotionConfig.ToKeyframeCurve(null));
        }

        /// <summary>Hình của cú "đóng dấu": 1,05 → ~1,12 → ~0,957 → 1.</summary>
        [Test]
        public void DropCurve_GivesTheStampShape()
        {
            KeyframeCurve curve = LeaderboardMotionConfig.ToKeyframeCurve(DropCurve());
            float peak = float.MinValue;
            float trough = float.MaxValue;
            for (int step = 0; step <= 400; step++)
            {
                float scale = Easing.LerpUnclamped(1.05f, 1f, curve.Evaluate(step / 400f));
                peak = Mathf.Max(peak, scale);
                trough = Mathf.Min(trough, scale);
            }
            Assert.AreEqual(1.123f, peak, 0.005f);
            Assert.AreEqual(0.957f, trough, 0.005f);
            Assert.AreEqual(1f, Easing.LerpUnclamped(1.05f, 1f, curve.Evaluate(1f)), 0.0001f);
        }

        private static void AssertParity(AnimationCurve unityCurve)
        {
            KeyframeCurve curve = LeaderboardMotionConfig.ToKeyframeCurve(unityCurve);
            for (int step = -10; step <= 410; step++)
            {
                float time = step / 400f;
                Assert.AreEqual(unityCurve.Evaluate(time), curve.Evaluate(time), Tolerance, "t = " + time);
            }
        }
    }
}
