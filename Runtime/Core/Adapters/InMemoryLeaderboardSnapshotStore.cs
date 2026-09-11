using System;
using System.Collections.Generic;

namespace DreamTech.Leaderboard
{
    /// <summary>Lưu snapshot trong RAM (mất khi tắt app). Dùng cho test hoặc game không cần nhớ lần xem.</summary>
    public sealed class InMemoryLeaderboardSnapshotStore : ILeaderboardSnapshotStore
    {
        private readonly Dictionary<string, RevealSnapshot> _snapshots =
            new Dictionary<string, RevealSnapshot>(StringComparer.Ordinal);

        public bool TryLoad(string boardId, out RevealSnapshot snapshot)
        {
            return _snapshots.TryGetValue(boardId ?? string.Empty, out snapshot);
        }

        public void Save(string boardId, RevealSnapshot snapshot)
        {
            _snapshots[boardId ?? string.Empty] = snapshot;
        }

        public void Clear(string boardId)
        {
            _snapshots.Remove(boardId ?? string.Empty);
        }
    }
}
