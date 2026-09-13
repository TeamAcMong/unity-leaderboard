using System;
using System.Collections.Generic;

namespace DreamTech.Leaderboard.League
{
    /// <summary>
    /// Bảng của nhóm tại một thời điểm. Standings đã sort giảm dần theo cúp, rank 0-based liên tục từ 0 — dùng lại
    /// <see cref="LeaderboardEntry"/> nên list, row và animation của leaderboard đọc được trực tiếp.
    /// </summary>
    public sealed class LeagueGroupSnapshot
    {
        private readonly LeaderboardEntry[] _standings;

        public LeagueGroupSnapshot(SeasonWindow season, int tierIndex, IEnumerable<LeaderboardEntry> standings, string localPlayerId)
        {
            Season = season ?? throw new ArgumentNullException(nameof(season));
            if (standings == null) throw new ArgumentNullException(nameof(standings));
            TierIndex = tierIndex;
            LocalPlayerId = localPlayerId ?? string.Empty;
            _standings = new List<LeaderboardEntry>(standings).ToArray();

            LocalRank = -1;
            for (int index = 0; index < _standings.Length; index++)
            {
                LeaderboardEntry entry = _standings[index];
                if (entry == null) throw new ArgumentException("Standings chứa entry null.", nameof(standings));
                if (entry.Rank != index) throw new ArgumentException("Standings phải có rank liên tục từ 0 (vị trí " + index + ").", nameof(standings));
                if (LocalRank < 0 && string.Equals(entry.PlayerId, LocalPlayerId, StringComparison.Ordinal)) LocalRank = index;
            }
        }

        public SeasonWindow Season { get; }
        public int TierIndex { get; }
        public string LocalPlayerId { get; }
        public IReadOnlyList<LeaderboardEntry> Standings => _standings;
        public int GroupSize => _standings.Length;

        /// <summary>-1 nếu người chơi không có trong nhóm.</summary>
        public int LocalRank { get; }

        public LeaderboardEntry LocalEntry => LocalRank >= 0 ? _standings[LocalRank] : null;
        public long LocalTrophies => LocalRank >= 0 ? _standings[LocalRank].Score : 0;
    }

    /// <summary>
    /// Một lần cộng cúp gửi lên dịch vụ. <see cref="GrantId"/> duy nhất để gửi lại bao nhiêu lần cũng chỉ cộng một lần
    /// (mất mạng giữa chừng, app bị tắt trước khi nhận phản hồi).
    /// </summary>
    public readonly struct LeagueTrophyGrant
    {
        public LeagueTrophyGrant(string grantId, string seasonId, int trophies)
        {
            if (string.IsNullOrEmpty(grantId)) throw new ArgumentException("Grant id không được rỗng.", nameof(grantId));
            if (string.IsNullOrEmpty(seasonId)) throw new ArgumentException("Season id không được rỗng.", nameof(seasonId));
            if (trophies <= 0) throw new ArgumentOutOfRangeException(nameof(trophies), "Số cúp phải dương.");
            GrantId = grantId;
            SeasonId = seasonId;
            Trophies = trophies;
        }

        public string GrantId { get; }

        /// <summary>Mùa lúc thắng. Gửi trễ sang mùa sau vẫn tính cho mùa này (nếu dịch vụ còn nhận).</summary>
        public string SeasonId { get; }

        public int Trophies { get; }
    }

    /// <summary>Thông tin một lần thắng level mà luật cúp cần. Game tự quy độ khó ra số (0 thường, 1 khó, 2 siêu khó...).</summary>
    public readonly struct LevelWinContext
    {
        public LevelWinContext(int levelNumber, int difficultyIndex)
        {
            LevelNumber = levelNumber;
            DifficultyIndex = difficultyIndex < 0 ? 0 : difficultyIndex;
        }

        public int LevelNumber { get; }
        public int DifficultyIndex { get; }
    }
}
