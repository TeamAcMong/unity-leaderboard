using System;

namespace DreamTech.Leaderboard.ViewModel
{
    /// <summary>Bản thuần C# của Mathf.SmoothDamp (cùng công thức), để camera bám có thể test không cần Unity.</summary>
    public static class SmoothDamping
    {
        public static float Step(float current, float target, ref float velocity, float smoothTime, float maximumSpeed, float deltaTime)
        {
            smoothTime = Math.Max(0.0001f, smoothTime);
            float omega = 2f / smoothTime;
            float x = omega * deltaTime;
            float exponential = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            float change = current - target;
            float originalTarget = target;
            float maximumChange = maximumSpeed * smoothTime;
            change = Easing.Clamp(change, -maximumChange, maximumChange);
            target = current - change;
            float temporary = (velocity + omega * change) * deltaTime;
            velocity = (velocity - omega * temporary) * exponential;
            float output = target + (change + temporary) * exponential;
            if (originalTarget - current > 0f == output > originalTarget)
            {
                output = originalTarget;
                velocity = (output - originalTarget) / deltaTime;
            }
            return output;
        }
    }
}
