using System;

namespace DreamTech.Leaderboard
{
    public enum RankChangeKind
    {
        /// <summary>Người chơi chưa từng được xem trên board này (hoặc sang mùa mới): diễn nhịp "NEW".</summary>
        NewEntry = 0,

        /// <summary>Không có gì mới: hạng và điểm như lần xem trước.</summary>
        Unchanged = 1,

        /// <summary>Cùng hạng nhưng điểm cao hơn: pill "BEST".</summary>
        ScoreImproved = 2,

        /// <summary>Lên hạng: diễn nhấc lên, (quay số), leo vượt từng người, hạ cánh.</summary>
        RankUp = 3,

        /// <summary>Bị người khác vượt nên tụt hạng. Diễn lặng lẽ, không ăn mừng.</summary>
        RankDown = 4,

        /// <summary>Không có dữ liệu người chơi (chưa từng submit): chỉ xem bảng.</summary>
        NoLocalEntry = 5,
    }

    /// <summary>Thay đổi của người chơi giữa lần xem trước (snapshot) và dữ liệu hiện tại.</summary>
    public readonly struct RankChange
    {
        private RankChange(RankChangeKind kind, int fromRank, int toRank, long fromScore, long toScore)
        {
            Kind = kind;
            FromRank = fromRank;
            ToRank = toRank;
            FromScore = fromScore;
            ToScore = toScore;
        }

        public RankChangeKind Kind { get; }

        /// <summary>Hạng 0-based lúc xem trước; -1 nếu không có.</summary>
        public int FromRank { get; }

        /// <summary>Hạng 0-based hiện tại; -1 nếu không có dữ liệu người chơi.</summary>
        public int ToRank { get; }

        public long FromScore { get; }
        public long ToScore { get; }

        /// <summary>Số người bị vượt (chỉ &gt; 0 khi RankUp).</summary>
        public int PassedCount => Kind == RankChangeKind.RankUp ? FromRank - ToRank : 0;

        /// <summary>Có cần diễn gì không (mọi loại trừ xem bảng không có người chơi).</summary>
        public bool HasLocalEntry => ToRank >= 0;

        public static RankChange NoLocalEntry => new RankChange(RankChangeKind.NoLocalEntry, -1, -1, 0, 0);

        /// <summary>Xem bảng thuần tuý, không so với snapshot.</summary>
        public static RankChange Browse(LeaderboardEntry current)
        {
            if (current == null) return NoLocalEntry;
            return new RankChange(RankChangeKind.Unchanged, current.Rank, current.Rank, current.Score, current.Score);
        }

        /// <summary>Tạo trực tiếp (cho test và cheat); luồng thật nên đi qua <see cref="Resolve"/>.</summary>
        public static RankChange Create(RankChangeKind kind, int fromRank, int toRank, long fromScore, long toScore)
        {
            return new RankChange(kind, fromRank, toRank, fromScore, toScore);
        }

        /// <summary>
        /// So snapshot đã xem với dữ liệu hiện tại. Snapshot khác mùa giải được coi như chưa từng xem.
        /// </summary>
        public static RankChange Resolve(RevealSnapshot? lastRevealed, LeaderboardEntry current, string seasonKey)
        {
            if (current == null) return NoLocalEntry;

            string season = seasonKey ?? string.Empty;
            if (!lastRevealed.HasValue || !string.Equals(lastRevealed.Value.SeasonKey, season, StringComparison.Ordinal))
            {
                return new RankChange(RankChangeKind.NewEntry, -1, current.Rank, 0, current.Score);
            }

            RevealSnapshot snapshot = lastRevealed.Value;
            if (current.Rank < snapshot.Rank)
            {
                return new RankChange(RankChangeKind.RankUp, snapshot.Rank, current.Rank, snapshot.Score, current.Score);
            }
            if (current.Rank > snapshot.Rank)
            {
                return new RankChange(RankChangeKind.RankDown, snapshot.Rank, current.Rank, snapshot.Score, current.Score);
            }
            if (current.Score > snapshot.Score)
            {
                return new RankChange(RankChangeKind.ScoreImproved, snapshot.Rank, current.Rank, snapshot.Score, current.Score);
            }
            return new RankChange(RankChangeKind.Unchanged, snapshot.Rank, current.Rank, snapshot.Score, current.Score);
        }

        public override string ToString()
        {
            return Kind + " " + FromRank + "->" + ToRank + " (" + FromScore + "->" + ToScore + ")";
        }
    }
}
