using System;
using System.Collections.Generic;

namespace DreamTech.Leaderboard
{
    /// <summary>
    /// Chỗ gặp nhau giữa các assembly: nơi lắp ráp (composition root của game) đăng ký board, các host khác tra theo id.
    ///
    /// <para>Widget KHÔNG tự tra registry — host lấy board rồi truyền vào, để phụ thuộc luôn nhìn thấy được.
    /// Chỉ dùng trên main thread. Hợp đồng giống các locator khác của dự án: Unregister chỉ gỡ đúng tham chiếu đã đăng ký,
    /// Reset trước khi reload assembly.</para>
    /// </summary>
    public static class LeaderboardBoardRegistry
    {
        private static readonly Dictionary<string, ILeaderboardBoard> Boards =
            new Dictionary<string, ILeaderboardBoard>(StringComparer.Ordinal);

        public static event Action<ILeaderboardBoard> BoardRegistered;

        public static void Register(ILeaderboardBoard board)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            Boards[board.BoardId] = board;
            BoardRegistered?.Invoke(board);
        }

        /// <summary>Chỉ gỡ khi đúng tham chiếu đang đăng ký, để một nơi cũ không gỡ nhầm board mới.</summary>
        public static bool Unregister(ILeaderboardBoard board)
        {
            if (board == null) return false;
            if (!Boards.TryGetValue(board.BoardId, out ILeaderboardBoard existing) || !ReferenceEquals(existing, board)) return false;
            return Boards.Remove(board.BoardId);
        }

        public static bool TryGet(string boardId, out ILeaderboardBoard board)
        {
            if (boardId == null)
            {
                board = null;
                return false;
            }
            return Boards.TryGetValue(boardId, out board);
        }

        public static void Reset()
        {
            Boards.Clear();
            BoardRegistered = null;
        }
    }
}
