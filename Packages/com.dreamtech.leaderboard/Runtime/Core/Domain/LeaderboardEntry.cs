using System.Globalization;

namespace DreamTech.Leaderboard
{
    /// <summary>
    /// Một dòng dữ liệu thật trên bảng xếp hạng. Bất biến: backend trả về bản mới thay vì sửa bản cũ,
    /// nhờ vậy view-model có thể giữ tham chiếu mà không sợ dữ liệu đổi ngầm giữa animation.
    /// Rank là 0-based (0 = hạng nhất), giống Unity Gaming Services; hiển thị thì +1.
    /// </summary>
    public sealed class LeaderboardEntry
    {
        public LeaderboardEntry(string playerId, string displayName, long score, int rank)
        {
            PlayerId = playerId;
            DisplayName = displayName;
            Score = score;
            Rank = rank;
        }

        public string PlayerId { get; }
        public string DisplayName { get; }
        public long Score { get; }

        /// <summary>Hạng 0-based.</summary>
        public int Rank { get; }

        /// <summary>Hạng hiển thị cho người chơi (1-based).</summary>
        public int OneBasedRank => Rank + 1;

        public LeaderboardEntry WithRank(int rank)
        {
            return new LeaderboardEntry(PlayerId, DisplayName, Score, rank);
        }

        public LeaderboardEntry WithScore(long score)
        {
            return new LeaderboardEntry(PlayerId, DisplayName, score, Rank);
        }

        public override string ToString()
        {
            return "#" + OneBasedRank.ToString(CultureInfo.InvariantCulture) + " " + DisplayName +
                   " (" + Score.ToString(CultureInfo.InvariantCulture) + ")";
        }
    }
}
