using System;

namespace DreamTech.Leaderboard.ViewModel
{
    /// <summary>
    /// Đường cong nhiều khoá, đánh giá GIỐNG HỆT <c>UnityEngine.AnimationCurve.Evaluate</c> — nhưng thuần C#, vì assembly
    /// view-model không được tham chiếu UnityEngine (<c>noEngineReferences</c>).
    ///
    /// <para><b>Vì sao cần.</b> Easing có sẵn (OutCubic, OutBack…) chỉ xấp xỉ được một đường cong designer vẽ tay. Muốn một
    /// nhịp khớp bản tham khảo tới từng khung (ví dụ cú "đóng dấu" 1,05 → 1,12 → 0,957 → 1, hay cú trượt vào
    /// có vọt 2,6 % ở 70 %), phải chạy đúng đường cong gốc với đúng khoá, tiếp tuyến và trọng số của nó. Lớp UI đổi
    /// <c>AnimationCurve</c> trong inspector sang kiểu này lúc chụp cấu hình.</para>
    ///
    /// <para>Quy tắc đánh giá của Unity: ngoài khoảng thì kẹp về khoá đầu/cuối; giữa hai khoá mà cả hai phía không có trọng số
    /// thì nội suy Hermite bậc ba với tiếp tuyến nhân độ dài đoạn; có trọng số ở một phía thì là đường Bézier bậc ba trên mặt
    /// phẳng (thời gian, giá trị), phía không có trọng số lấy 1/3, và giải thời gian → tham số bằng chia đôi. Tiếp tuyến vô cực
    /// = bậc thang (giữ giá trị khoá trái).</para>
    /// </summary>
    public sealed class KeyframeCurve
    {
        /// <summary>Trọng số mặc định của Unity cho phía tiếp tuyến không bật trọng số.</summary>
        public const float DefaultWeight = 1f / 3f;

        private const int BisectionSteps = 40;

        private readonly Key[] _keys;

        public KeyframeCurve(params Key[] keys)
        {
            if (keys == null || keys.Length == 0) throw new ArgumentException("Đường cong cần ít nhất một khoá.", nameof(keys));
            _keys = (Key[])keys.Clone();
            Array.Sort(_keys, (left, right) => left.Time.CompareTo(right.Time));
        }

        public int Length => _keys.Length;

        public Key this[int index] => _keys[index];

        public float Evaluate(float time)
        {
            Key first = _keys[0];
            if (_keys.Length == 1 || time <= first.Time) return first.Value;
            Key last = _keys[_keys.Length - 1];
            if (time >= last.Time) return last.Value;

            for (int index = 0; index < _keys.Length - 1; index++)
            {
                Key left = _keys[index];
                Key right = _keys[index + 1];
                if (time > right.Time) continue;
                return EvaluateSegment(left, right, time);
            }
            return last.Value;
        }

        private static float EvaluateSegment(Key left, Key right, float time)
        {
            float span = right.Time - left.Time;
            if (span <= 0f) return right.Value;
            if (float.IsInfinity(left.OutSlope) || float.IsInfinity(right.InSlope)) return left.Value;

            bool weightedOut = (left.Weighted & WeightedMode.Out) != 0;
            bool weightedIn = (right.Weighted & WeightedMode.In) != 0;
            float outWeight = weightedOut ? left.OutWeight : DefaultWeight;
            float inWeight = weightedIn ? right.InWeight : DefaultWeight;

            if (!weightedOut && !weightedIn)
            {
                float s = (time - left.Time) / span;
                float s2 = s * s;
                float s3 = s2 * s;
                return (2f * s3 - 3f * s2 + 1f) * left.Value
                       + (s3 - 2f * s2 + s) * span * left.OutSlope
                       + (-2f * s3 + 3f * s2) * right.Value
                       + (s3 - s2) * span * right.InSlope;
            }

            // Bézier trên (thời gian, giá trị): tay nắm dài bằng trọng số × độ dài đoạn theo trục thời gian.
            float x0 = left.Time;
            float x1 = left.Time + outWeight * span;
            float x2 = right.Time - inWeight * span;
            float x3 = right.Time;
            float y0 = left.Value;
            float y1 = left.Value + left.OutSlope * outWeight * span;
            float y2 = right.Value - right.InSlope * inWeight * span;
            float y3 = right.Value;

            float low = 0f;
            float high = 1f;
            for (int step = 0; step < BisectionSteps; step++)
            {
                float middle = (low + high) * 0.5f;
                if (Bezier(middle, x0, x1, x2, x3) < time) low = middle;
                else high = middle;
            }
            return Bezier((low + high) * 0.5f, y0, y1, y2, y3);
        }

        private static float Bezier(float u, float p0, float p1, float p2, float p3)
        {
            float v = 1f - u;
            return v * v * v * p0 + 3f * v * v * u * p1 + 3f * v * u * u * p2 + u * u * u * p3;
        }

        [Flags]
        public enum WeightedMode
        {
            None = 0,
            In = 1,
            Out = 2,
            Both = 3,
        }

        /// <summary>Một khoá — cùng ý nghĩa field với <c>UnityEngine.Keyframe</c>.</summary>
        public readonly struct Key
        {
            public Key(float time, float value, float inSlope, float outSlope,
                       WeightedMode weighted = WeightedMode.None, float inWeight = DefaultWeight, float outWeight = DefaultWeight)
            {
                Time = time;
                Value = value;
                InSlope = inSlope;
                OutSlope = outSlope;
                Weighted = weighted;
                InWeight = inWeight;
                OutWeight = outWeight;
            }

            public float Time { get; }
            public float Value { get; }
            public float InSlope { get; }
            public float OutSlope { get; }
            public WeightedMode Weighted { get; }
            public float InWeight { get; }
            public float OutWeight { get; }
        }
    }
}
