using System;
using System.Globalization;
using UnityEngine;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>
    /// Lưu snapshot "lần xem cuối" vào PlayerPrefs. Mất snapshot chỉ làm lần mở sau diễn lại một lần, nên không cần cloud.
    /// </summary>
    public sealed class PlayerPrefsLeaderboardSnapshotStore : ILeaderboardSnapshotStore
    {
        private const char Separator = '|';
        private readonly string _keyPrefix;

        public PlayerPrefsLeaderboardSnapshotStore(string keyPrefix = "dreamtech.leaderboard.snapshot.")
        {
            _keyPrefix = keyPrefix ?? string.Empty;
        }

        public bool TryLoad(string boardId, out RevealSnapshot snapshot)
        {
            snapshot = default;
            string stored = PlayerPrefs.GetString(KeyFor(boardId), string.Empty);
            if (string.IsNullOrEmpty(stored)) return false;

            string[] parts = stored.Split(new[] { Separator }, 3);
            if (parts.Length != 3) return false;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rank)) return false;
            if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long score)) return false;

            snapshot = new RevealSnapshot(rank, score, parts[2]);
            return true;
        }

        public void Save(string boardId, RevealSnapshot snapshot)
        {
            string value = snapshot.Rank.ToString(CultureInfo.InvariantCulture) + Separator +
                           snapshot.Score.ToString(CultureInfo.InvariantCulture) + Separator +
                           (snapshot.SeasonKey ?? string.Empty);
            PlayerPrefs.SetString(KeyFor(boardId), value);
            PlayerPrefs.Save();
        }

        public void Clear(string boardId)
        {
            PlayerPrefs.DeleteKey(KeyFor(boardId));
        }

        private string KeyFor(string boardId)
        {
            if (string.IsNullOrEmpty(boardId)) throw new ArgumentException("Board id không được rỗng.", nameof(boardId));
            return _keyPrefix + boardId;
        }
    }
}
