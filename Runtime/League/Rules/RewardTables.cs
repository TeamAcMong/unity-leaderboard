using System;
using System.Collections.Generic;

namespace DreamTech.Leaderboard.League
{
    /// <summary>Bảng thưởng cuối mùa: tier + hạng → gói quà. Trả <see cref="LeagueRewardPackage.None"/> khi không có thưởng.</summary>
    public interface ILeagueRewardTable
    {
        LeagueRewardPackage RewardFor(int tierIndex, int rank, int groupSize);
    }

    /// <summary>Không phát gì — dùng khi game chưa có thưởng.</summary>
    public sealed class EmptyLeagueRewardTable : ILeagueRewardTable
    {
        public LeagueRewardPackage RewardFor(int tierIndex, int rank, int groupSize)
        {
            return LeagueRewardPackage.None;
        }
    }

    /// <summary>Một khoảng hạng [FirstRank, LastRank] (0-based, gồm cả hai đầu) được một gói quà.</summary>
    public sealed class LeagueRewardBracket
    {
        public const int AnyTier = -1;
        public const int ToLastRank = -1;

        public LeagueRewardBracket(int tierIndex, int firstRank, int lastRank, LeagueRewardPackage package)
        {
            if (firstRank < 0) throw new ArgumentOutOfRangeException(nameof(firstRank), "Hạng đầu không được âm.");
            if (lastRank != ToLastRank && lastRank < firstRank)
            {
                throw new ArgumentOutOfRangeException(nameof(lastRank), "Hạng cuối phải >= hạng đầu (hoặc ToLastRank).");
            }
            TierIndex = tierIndex < 0 ? AnyTier : tierIndex;
            FirstRank = firstRank;
            LastRank = lastRank;
            Package = package ?? throw new ArgumentNullException(nameof(package));
        }

        /// <summary><see cref="AnyTier"/> = áp cho mọi tier.</summary>
        public int TierIndex { get; }

        public int FirstRank { get; }

        /// <summary><see cref="ToLastRank"/> = tới cuối nhóm.</summary>
        public int LastRank { get; }

        public LeagueRewardPackage Package { get; }

        public bool Matches(int tierIndex, int rank)
        {
            if (TierIndex != AnyTier && TierIndex != tierIndex) return false;
            if (rank < FirstRank) return false;
            return LastRank == ToLastRank || rank <= LastRank;
        }
    }

    /// <summary>Mặc định: danh sách khoảng hạng, khoảng khớp ĐẦU TIÊN thắng — đặt khoảng riêng của tier lên trước khoảng chung.</summary>
    public sealed class RankBracketRewardTable : ILeagueRewardTable
    {
        private readonly LeagueRewardBracket[] _brackets;

        public RankBracketRewardTable(IEnumerable<LeagueRewardBracket> brackets)
        {
            if (brackets == null) throw new ArgumentNullException(nameof(brackets));
            var list = new List<LeagueRewardBracket>();
            foreach (LeagueRewardBracket bracket in brackets)
            {
                if (bracket == null) throw new ArgumentException("Bảng thưởng chứa khoảng null.", nameof(brackets));
                list.Add(bracket);
            }
            _brackets = list.ToArray();
        }

        public IReadOnlyList<LeagueRewardBracket> Brackets => _brackets;

        public LeagueRewardPackage RewardFor(int tierIndex, int rank, int groupSize)
        {
            if (rank < 0 || rank >= groupSize) return LeagueRewardPackage.None;
            for (int index = 0; index < _brackets.Length; index++)
            {
                if (_brackets[index].Matches(tierIndex, rank)) return _brackets[index].Package;
            }
            return LeagueRewardPackage.None;
        }
    }
}
