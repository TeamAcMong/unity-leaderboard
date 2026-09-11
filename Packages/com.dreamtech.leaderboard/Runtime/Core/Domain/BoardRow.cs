namespace DreamTech.Leaderboard
{
    /// <summary>Một dòng trong danh sách hiển thị: hoặc một người chơi, hoặc dòng "..." đánh dấu chỗ đứt quãng giữa hai đoạn hạng.</summary>
    public readonly struct BoardRow
    {
        private BoardRow(LeaderboardEntry entry, bool isGap, bool isLocalPlayer)
        {
            Entry = entry;
            IsGap = isGap;
            IsLocalPlayer = isLocalPlayer;
        }

        /// <summary>Null khi là dòng "...".</summary>
        public LeaderboardEntry Entry { get; }

        public bool IsGap { get; }
        public bool IsLocalPlayer { get; }

        public static BoardRow ForEntry(LeaderboardEntry entry, bool isLocalPlayer)
        {
            return new BoardRow(entry, false, isLocalPlayer);
        }

        public static BoardRow Gap()
        {
            return new BoardRow(null, true, false);
        }
    }
}
