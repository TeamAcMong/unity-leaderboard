namespace DreamTech.Leaderboard
{
    /// <summary>
    /// Nhịp phản hồi không-hình-ảnh (âm thanh, haptic, analytics). CHỈ THÊM VÀO CUỐI: giá trị đã có không được đổi số,
    /// vì sink của game có thể đã serialize hoặc switch theo số.
    /// </summary>
    public enum LeaderboardBeat
    {
        RevealStarted = 0,
        Lift = 1,
        SpinTick = 2,
        Pass = 3,
        Land = 4,
        NewEntry = 5,
        ScoreImproved = 6,
        Celebrate = 7,
        Skipped = 8,
        RevealFinished = 9,

        /// <summary>
        /// Row mình vừa tới ranh giới của phần HOST tự trình bày (<c>MotionSettings.HostPresentedTopRanks</c>, ví dụ bục top 3)
        /// và màn diễn DỪNG ở đó chờ host diễn cú lên bục (thanh thành cờ, hai cờ đổi chỗ) rồi gọi <c>ReleasePodiumHold()</c>.
        ///
        /// <para>Chỉ phát khi cờ bật và màn LÊN HẠNG đáp vào phần đó; đúng một lần, sau mọi nhịp Pass của đoạn leo trong list và
        /// trước Land. Bị bỏ qua trước khi tới ranh giới thì KHÔNG phát — host nhận Skipped và dựng thẳng trạng thái cuối.</para>
        /// </summary>
        PodiumTakeover = 10,
    }

    /// <summary>Ngữ cảnh đi kèm một nhịp. Sink tự quy ra cao độ âm / độ mạnh rung.</summary>
    public readonly struct LeaderboardBeatContext
    {
        public LeaderboardBeatContext(RankTier tier, int passIndex, int passTotal, float progress, int fromRank, int toRank)
        {
            Tier = tier;
            PassIndex = passIndex;
            PassTotal = passTotal;
            Progress = progress;
            FromRank = fromRank;
            ToRank = toRank;
        }

        public RankTier Tier { get; }

        /// <summary>Người thứ mấy vừa bị vượt (1-based) khi beat là Pass.</summary>
        public int PassIndex { get; }

        public int PassTotal { get; }

        /// <summary>0..1 tiến độ của pha hiện tại (dùng cho SpinTick).</summary>
        public float Progress { get; }

        public int FromRank { get; }
        public int ToRank { get; }
    }

    /// <summary>
    /// Port nhận nhịp phản hồi. Một widget có thể có nhiều sink (âm thanh + haptic + analytics). Sink không được ném
    /// exception; widget vẫn bắt riêng từng sink để một sink hỏng không làm hỏng màn diễn.
    /// </summary>
    public interface ILeaderboardFeedbackSink
    {
        void OnBeat(LeaderboardBeat beat, in LeaderboardBeatContext context);
    }
}
