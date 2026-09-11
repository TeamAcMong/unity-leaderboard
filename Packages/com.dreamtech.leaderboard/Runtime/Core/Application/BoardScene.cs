using System.Collections.Generic;

namespace DreamTech.Leaderboard
{
    /// <summary>Chế độ trình bày khi mở board.</summary>
    public enum BoardPresentMode
    {
        /// <summary>Chỉ xem bảng, không diễn gì.</summary>
        Browse = 0,

        /// <summary>Nếu có thay đổi chưa được xem thì diễn (lên hạng, NEW, BEST...), không thì chỉ nhún nhẹ row của mình.</summary>
        RevealIfPending = 1,
    }

    /// <summary>Mọi thứ một widget cần để dựng và diễn một lần hiển thị. Bất biến sau khi tạo.</summary>
    public sealed class BoardScene
    {
        public BoardScene(string boardId, IReadOnlyList<BoardRow> rows, int localRowIndex, RankChange change,
                          RankTierRule tierRule, FetchPlan plan, BoardPresentMode mode)
        {
            BoardId = boardId;
            Rows = rows;
            LocalRowIndex = localRowIndex;
            Change = change;
            TierRule = tierRule;
            Plan = plan;
            Mode = mode;
        }

        public string BoardId { get; }
        public IReadOnlyList<BoardRow> Rows { get; }

        /// <summary>Chỉ số dòng người chơi; -1 nếu không có.</summary>
        public int LocalRowIndex { get; }

        public RankChange Change { get; }
        public RankTierRule TierRule { get; }
        public FetchPlan Plan { get; }
        public BoardPresentMode Mode { get; }

        public LeaderboardEntry LocalEntry => LocalRowIndex >= 0 ? Rows[LocalRowIndex].Entry : null;

        /// <summary>Có cần diễn một màn reveal hay chỉ hiện bảng.</summary>
        public bool NeedsReveal => Mode == BoardPresentMode.RevealIfPending && LocalRowIndex >= 0;
    }
}
