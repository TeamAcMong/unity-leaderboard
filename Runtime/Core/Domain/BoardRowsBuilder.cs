using System;
using System.Collections.Generic;

namespace DreamTech.Leaderboard
{
    /// <summary>
    /// Gộp các đoạn dữ liệu (top + quanh người chơi) thành danh sách dòng hiển thị liền mạch.
    /// </summary>
    public static class BoardRowsBuilder
    {
        /// <summary>
        /// Bỏ trùng theo PlayerId (entry của người chơi luôn thắng vì là dữ liệu mới nhất), sort đầy đủ để kết quả
        /// không phụ thuộc thứ tự backend trả về, rồi chèn dòng "..." ở mọi chỗ hạng bị nhảy cóc.
        /// </summary>
        public static List<BoardRow> Build(IReadOnlyList<LeaderboardEntry> top, IReadOnlyList<LeaderboardEntry> window,
                                           LeaderboardEntry localEntry, string localPlayerId)
        {
            var entriesById = new Dictionary<string, LeaderboardEntry>(StringComparer.Ordinal);
            AddAll(entriesById, top);
            AddAll(entriesById, window);
            if (localEntry != null && localEntry.PlayerId != null) entriesById[localEntry.PlayerId] = localEntry;

            var entries = new List<LeaderboardEntry>(entriesById.Values);
            entries.Sort(CompareEntries);

            var rows = new List<BoardRow>(entries.Count + 2);
            int previousRank = -1;
            for (int index = 0; index < entries.Count; index++)
            {
                LeaderboardEntry entry = entries[index];
                if (previousRank >= 0 && entry.Rank > previousRank + 1) rows.Add(BoardRow.Gap());
                bool isLocal = localPlayerId != null && string.Equals(entry.PlayerId, localPlayerId, StringComparison.Ordinal);
                rows.Add(BoardRow.ForEntry(entry, isLocal));
                previousRank = entry.Rank;
            }
            return rows;
        }

        /// <summary>Hạng tăng dần, cùng hạng thì điểm giảm dần, cùng nữa thì theo PlayerId để luôn ổn định.</summary>
        public static int CompareEntries(LeaderboardEntry first, LeaderboardEntry second)
        {
            if (first.Rank != second.Rank) return first.Rank.CompareTo(second.Rank);
            if (first.Score != second.Score) return second.Score.CompareTo(first.Score);
            return string.CompareOrdinal(first.PlayerId, second.PlayerId);
        }

        /// <summary>Chỉ số dòng của người chơi; -1 nếu không có.</summary>
        public static int IndexOfLocal(IReadOnlyList<BoardRow> rows)
        {
            for (int index = 0; index < rows.Count; index++)
            {
                if (rows[index].IsLocalPlayer) return index;
            }
            return -1;
        }

        private static void AddAll(Dictionary<string, LeaderboardEntry> entriesById, IReadOnlyList<LeaderboardEntry> source)
        {
            if (source == null) return;
            for (int index = 0; index < source.Count; index++)
            {
                LeaderboardEntry entry = source[index];
                if (entry != null && entry.PlayerId != null) entriesById[entry.PlayerId] = entry;
            }
        }
    }
}
