using System;

namespace DreamTech.Leaderboard.ViewModel
{
    /// <summary>Easing và nội suy tối thiểu, thuần C# (không Mathf), không phụ thuộc DOTween/PrimeTween. t trong [0, 1].</summary>
    public static class Easing
    {
        public const float DefaultBackOvershoot = 1.70158f;

        public static float Clamp01(float value)
        {
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }

        public static float Clamp(float value, float minimum, float maximum)
        {
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        public static float Lerp(float from, float to, float t)
        {
            return from + (to - from) * Clamp01(t);
        }

        public static float LerpUnclamped(float from, float to, float t)
        {
            return from + (to - from) * t;
        }

        public static long LerpLong(long from, long to, float t)
        {
            return from + (long)((to - from) * (double)Clamp01(t));
        }

        public static float OutQuad(float t)
        {
            return 1f - (1f - t) * (1f - t);
        }

        public static float OutCubic(float t)
        {
            float remaining = 1f - t;
            return 1f - remaining * remaining * remaining;
        }

        public static float InOutCubic(float t)
        {
            return t < 0.5f ? 4f * t * t * t : 1f - (float)Math.Pow(-2f * t + 2f, 3f) * 0.5f;
        }

        /// <summary>
        /// Họ InOut theo luỹ thừa: <paramref name="power"/> = 2 là InOutQuad, 3 là InOutCubic. Tốc độ đỉnh (giữa đường)
        /// bằng <paramref name="power"/> lần tốc độ trung bình — số càng lớn càng dồn chuyển động vào giữa.
        /// </summary>
        public static float InOutPower(float t, float power)
        {
            if (power <= 1f) return Clamp01(t);
            return t < 0.5f
                ? 0.5f * (float)Math.Pow(2f * t, power)
                : 1f - 0.5f * (float)Math.Pow(2f - 2f * t, power);
        }

        /// <summary>Vượt quá đích rồi bật lại. Overshoot càng lớn càng nảy.</summary>
        public static float OutBack(float t, float overshoot)
        {
            float cubicFactor = overshoot + 1f;
            float shifted = t - 1f;
            return 1f + cubicFactor * shifted * shifted * shifted + overshoot * shifted * shifted;
        }
    }
}
