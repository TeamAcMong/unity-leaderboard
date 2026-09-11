namespace DreamTech.Leaderboard.ViewModel
{
    /// <summary>
    /// Một hiệu ứng có mốc bắt đầu và thời lượng, đo theo đồng hồ của <see cref="BoardModel"/>.
    /// Hiệu ứng là DỮ LIỆU chứ không phải coroutine trên view: view nào render cũng suy ra đúng pha từ đồng hồ,
    /// nên view bị tái sử dụng giữa chừng vẫn diễn tiếp y hệt.
    /// </summary>
    public struct TimedEffect
    {
        public double StartTime { get; private set; }
        public float Duration { get; private set; }
        public bool IsStarted { get; private set; }

        public void Start(double now, float duration)
        {
            StartTime = now;
            Duration = duration < 0f ? 0f : duration;
            IsStarted = true;
        }

        public void Stop()
        {
            IsStarted = false;
        }

        public bool IsRunning(double now)
        {
            return IsStarted && now - StartTime < Duration;
        }

        public float Elapsed(double now)
        {
            return IsStarted ? (float)(now - StartTime) : 0f;
        }

        /// <summary>0..1; chưa bắt đầu hoặc thời lượng 0 thì coi như đã xong (1).</summary>
        public float Progress(double now)
        {
            if (!IsStarted || Duration <= 0f) return 1f;
            return Easing.Clamp01((float)((now - StartTime) / Duration));
        }
    }

    /// <summary>Nội dung pill bật ra phía trên badge lúc hạ cánh.</summary>
    public enum PillContent
    {
        /// <summary>"▲N": số người vừa vượt.</summary>
        RankUp = 0,

        /// <summary>"NEW": lần đầu có mặt.</summary>
        New = 1,

        /// <summary>"BEST": cùng hạng, điểm tốt hơn.</summary>
        Best = 2,
    }
}
