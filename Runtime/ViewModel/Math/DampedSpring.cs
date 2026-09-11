using System;

namespace DreamTech.Leaderboard.ViewModel
{
    /// <summary>
    /// Lò xo tắt dần. Đẩy Target là giá trị tự vọt tới rồi lắng về một cách tự nhiên, không cần keyframe.
    /// Field công khai (không phải property) để Step() sửa trực tiếp trong struct.
    /// </summary>
    public struct DampedSpring
    {
        private const float MaximumSubstep = 0.016f;
        private const float SettledDistance = 0.0005f;
        private const float SettledVelocity = 0.005f;

        public float Value;
        public float Velocity;
        public float Target;

        /// <param name="stiffness">Độ cứng. Cao = bật về nhanh.</param>
        /// <param name="damping">Giảm chấn. Thấp = lắc nhiều lần.</param>
        public void Step(float deltaTime, float stiffness, float damping)
        {
            if (deltaTime <= 0f) return;
            // Chia nhỏ bước để ổn định cả khi frame bị giật.
            int substepCount = Math.Max(1, (int)Math.Ceiling(deltaTime / MaximumSubstep));
            float substep = deltaTime / substepCount;
            for (int index = 0; index < substepCount; index++)
            {
                float acceleration = -stiffness * (Value - Target) - damping * Velocity;
                Velocity += acceleration * substep;
                Value += Velocity * substep;
            }
        }

        public bool IsSettled => Math.Abs(Value - Target) < SettledDistance && Math.Abs(Velocity) < SettledVelocity;

        public void Reset(float value)
        {
            Value = value;
            Target = value;
            Velocity = 0f;
        }
    }
}
