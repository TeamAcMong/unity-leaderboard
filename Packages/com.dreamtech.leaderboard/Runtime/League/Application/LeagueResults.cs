using System;
using System.Collections.Generic;

namespace DreamTech.Leaderboard.League
{
    /// <summary>Kết quả một lần thắng level: màn Win đọc để diễn cúp bay + streak sáng thêm.</summary>
    public readonly struct LevelWinOutcome
    {
        public LevelWinOutcome(bool awarded, int trophies, WinStreakState streakBefore, WinStreakState streakAfter, string grantId)
        {
            Awarded = awarded;
            Trophies = trophies;
            StreakBefore = streakBefore;
            StreakAfter = streakAfter;
            GrantId = grantId;
        }

        /// <summary>False khi League đang khoá: không cộng cúp, không đổi streak.</summary>
        public bool Awarded { get; }

        public int Trophies { get; }
        public WinStreakState StreakBefore { get; }
        public WinStreakState StreakAfter { get; }

        /// <summary>Id lần cộng cúp; null nếu không có cúp.</summary>
        public string GrantId { get; }

        public bool StreakLeveledUp => StreakAfter.Level > StreakBefore.Level;
    }

    /// <summary>Kết quả báo một sự kiện streak (thoát, thua, retry, revive).</summary>
    public readonly struct WinStreakChange
    {
        public WinStreakChange(WinStreakState before, WinStreakState after)
        {
            Before = before;
            After = after;
        }

        public WinStreakState Before { get; }
        public WinStreakState After { get; }
        public bool Changed => !Before.Equals(After);
        public bool WasLost => !Before.IsEmpty && After.Level < Before.Level;
    }

    public enum LeagueClaimStatus
    {
        /// <summary>Không có quà (mùa không có thưởng, hoặc đã nhận trước đó).</summary>
        NothingToClaim = 0,

        /// <summary>Đã phát vào kho đồ của game.</summary>
        Granted = 1,

        /// <summary>Đã chốt với dịch vụ nhưng game chưa nhận được; sẽ phát lại ở <see cref="LeagueSystem.GrantPendingRewards"/>.</summary>
        Deferred = 2,
    }

    public readonly struct LeagueClaimOutcome
    {
        public LeagueClaimOutcome(LeagueClaimStatus status, LeagueRewardPackage package)
        {
            Status = status;
            Package = package ?? LeagueRewardPackage.None;
        }

        public LeagueClaimStatus Status { get; }
        public LeagueRewardPackage Package { get; }
    }

    /// <summary>Một dòng trên trang League: entry + vùng + gói quà nếu mùa kết thúc ngay bây giờ.</summary>
    public readonly struct LeagueStandingRow
    {
        public LeagueStandingRow(LeaderboardEntry entry, LeagueZone zone, LeagueRewardPackage reward, bool isLocal)
        {
            Entry = entry;
            Zone = zone;
            Reward = reward ?? LeagueRewardPackage.None;
            IsLocal = isLocal;
        }

        public LeaderboardEntry Entry { get; }
        public LeagueZone Zone { get; }
        public LeagueRewardPackage Reward { get; }
        public bool IsLocal { get; }
    }

    /// <summary>Mọi thứ trang League cần vẽ, chụp tại một thời điểm.</summary>
    public sealed class LeaguePageData
    {
        private readonly LeagueStandingRow[] _rows;

        internal LeaguePageData(LeagueGroupSnapshot group, LeagueRules rules, WinStreakState streak, WinStreakLadder streakLadder,
                                DateTime nowUtc, int unsentTrophies)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            Group = group;
            Season = group.Season;
            TimeLeft = group.Season.TimeLeft(nowUtc);
            TierIndex = rules.Ladder.ClampIndex(group.TierIndex);
            Tier = rules.Ladder.TierAt(TierIndex);
            Bands = rules.BandsFor(TierIndex, group.GroupSize);
            Streak = streak;
            StreakMultiplier = streakLadder.MultiplierAt(streak.Level);
            UnsentTrophies = unsentTrophies;

            var rows = new List<LeagueStandingRow>(group.GroupSize);
            for (int rank = 0; rank < group.GroupSize; rank++)
            {
                rows.Add(new LeagueStandingRow(group.Standings[rank], Bands.ZoneOf(rank), rules.RewardFor(TierIndex, rank, group.GroupSize),
                                               rank == group.LocalRank));
            }
            _rows = rows.ToArray();
        }

        public LeagueGroupSnapshot Group { get; }
        public SeasonWindow Season { get; }
        public TimeSpan TimeLeft { get; }
        public int TierIndex { get; }
        public LeagueTierDefinition Tier { get; }
        public LeagueZoneBands Bands { get; }
        public IReadOnlyList<LeagueStandingRow> Rows => _rows;

        /// <summary>-1 nếu người chơi không có trong nhóm.</summary>
        public int LocalRowIndex => Group.LocalRank;

        public WinStreakState Streak { get; }
        public int StreakMultiplier { get; }

        /// <summary>Cúp đã thắng nhưng chưa gửi được (mất mạng) — chưa nằm trong điểm trên bảng.</summary>
        public int UnsentTrophies { get; }
    }
}
