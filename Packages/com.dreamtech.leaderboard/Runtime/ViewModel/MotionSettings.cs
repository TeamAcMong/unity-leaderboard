using System;

namespace DreamTech.Leaderboard.ViewModel
{
    /// <summary>
    /// Mọi tham số thời gian/chuyển động mà view-model dùng. Widget chụp một bản sao lúc bắt đầu diễn, nên chỉnh config
    /// giữa chừng (inspector, remote config) không làm lệch một màn đang chạy.
    ///
    /// <para>Mặc định giữ đúng số của bản tham khảo "premium smooth": animation ngắn, mượt, một tiêu điểm là row người chơi.</para>
    /// <para>[Serializable] để config ScriptableObject nhúng thẳng vào inspector; bản thân class vẫn thuần C#.</para>
    /// </summary>
    [Serializable]
    public sealed class MotionSettings
    {
        // ---------------------------------------------------------------- Màn diễn
        public float IntroWait = 0.3f;
        public float LiftDuration = 0.22f;
        public float LiftScale = 1.05f;
        public float ClimbSecondsPerRow = 0.09f;
        public float ClimbDurationMinimum = 0.5f;
        public float ClimbDurationMaximum = 1.4f;
        public float SpinDuration = 0.7f;
        public float PassSlideDuration = 0.26f;

        /// <summary>Người phía trên nhường chỗ khi row mình đi được bao nhiêu phần ô. Nhỏ = nhường sớm, mượt hơn.</summary>
        public float MakeRoomAt = 0.3f;

        public float LandDuration = 0.32f;
        public float LandOvershoot = 1.3f;
        public float LandFlashAlpha = 0.35f;
        public float FlashDuration = 0.3f;
        public float ScoreCountDuration = 0.6f;
        public float NewEntryPopDuration = 0.4f;
        public float NewEntryPopOvershoot = 1.6f;
        public float BobDuration = 0.36f;
        public float BobAmplitude = 0.035f;
        public float MinimumPassBeatInterval = 0.05f;
        public float MinimumSpinBeatInterval = 0.05f;

        // ---------------------------------------------------------------- Intro của list
        public float IntroStagger = 0.03f;
        public float IntroDuration = 0.32f;
        public float IntroOffset = 36f;
        public float MaximumIntroDelay = 0.36f;
        public float IntroStartScale = 0.96f;
        public float IntroOvershoot = 1.2f;

        // ---------------------------------------------------------------- Camera
        public float FollowSmoothTime = 0.12f;
        public float ScrollToLocalDuration = 0.45f;

        // ---------------------------------------------------------------- Hiệu ứng trên row
        public float PillPopDuration = 0.3f;
        public float PillHoldDuration = 1.1f;
        public float PillRiseDuration = 0.3f;
        public float ShineDuration = 0.55f;
        public float RankRollDuration = 0.12f;
        public float BadgePunchDuration = 0.35f;

        public float PillTotalDuration => PillPopDuration + PillHoldDuration + PillRiseDuration;

        public MotionSettings Clone()
        {
            return (MotionSettings)MemberwiseClone();
        }
    }
}
