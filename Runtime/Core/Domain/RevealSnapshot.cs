namespace DreamTech.Leaderboard
{
    /// <summary>
    /// Trạng thái người chơi ĐÃ ĐƯỢC XEM lần cuối trên một board (hạng + điểm + mùa giải).
    ///
    /// <para>Thay cho mô hình "giữ thay đổi đang chờ trong RAM": so snapshot đã lưu với dữ liệu hiện tại là biết còn
    /// rank-up chưa diễn. Cách này sống sót qua việc tắt app, tự gộp nhiều lần thắng thành một lần diễn, và mọi nơi
    /// hiển thị (popup, màn Win, Home) đều thấy cùng một sự thật.</para>
    /// </summary>
    public readonly struct RevealSnapshot
    {
        public RevealSnapshot(int rank, long score, string seasonKey)
        {
            Rank = rank;
            Score = score;
            SeasonKey = seasonKey ?? string.Empty;
        }

        /// <summary>Hạng 0-based lúc được xem.</summary>
        public int Rank { get; }

        public long Score { get; }

        /// <summary>Khoá mùa giải lúc được xem. Khác mùa hiện tại thì snapshot coi như không tồn tại.</summary>
        public string SeasonKey { get; }
    }
}
