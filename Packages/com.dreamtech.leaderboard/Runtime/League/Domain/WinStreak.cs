using System;
using System.Collections.Generic;

namespace DreamTech.Leaderboard.League
{
    /// <summary>
    /// Những việc trong level có thể làm đổi streak. Game báo sự kiện, luật (<see cref="IWinStreakRule"/>) quyết định hệ quả —
    /// nhờ vậy đổi luật không phải sửa màn Win, popup thoát hay popup thua. Chỉ thêm giá trị vào cuối.
    /// </summary>
    public enum WinStreakEvent
    {
        /// <summary>Thắng level. Đi qua <see cref="LeagueSystem.RecordLevelWin"/> vì còn phải tính cúp.</summary>
        LevelWon = 0,

        /// <summary>Thua hẳn: người chơi đã từ chối revive / đóng popup thua cuối cùng.</summary>
        LevelLost = 1,

        /// <summary>Thoát level giữa chừng về Home.</summary>
        LevelQuit = 2,

        /// <summary>Chơi lại level giữa chừng (bỏ lượt đang chơi).</summary>
        LevelRetried = 3,

        /// <summary>Hồi sinh để chơi tiếp sau khi hết nước.</summary>
        LevelRevived = 4,
    }

    /// <summary>Trạng thái streak. Level 0 = chưa có streak.</summary>
    public readonly struct WinStreakState : IEquatable<WinStreakState>
    {
        public WinStreakState(int level, int winsTowardNextLevel)
        {
            Level = level < 0 ? 0 : level;
            WinsTowardNextLevel = winsTowardNextLevel < 0 ? 0 : winsTowardNextLevel;
        }

        public static WinStreakState Empty => default;

        public int Level { get; }

        /// <summary>Số trận thắng đã tích cho bậc kế tiếp (luật cần nhiều trận mỗi bậc mới dùng).</summary>
        public int WinsTowardNextLevel { get; }

        public bool IsEmpty => Level == 0 && WinsTowardNextLevel == 0;

        public bool Equals(WinStreakState other)
        {
            return Level == other.Level && WinsTowardNextLevel == other.WinsTowardNextLevel;
        }

        public override bool Equals(object obj)
        {
            return obj is WinStreakState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Level * 397) ^ WinsTowardNextLevel;
        }

        public override string ToString()
        {
            return "streak L" + Level + " (+" + WinsTowardNextLevel + ")";
        }
    }

    /// <summary>Một bậc streak: hệ số cúp và quà (có thể rỗng) khi chạm bậc.</summary>
    public sealed class WinStreakStep
    {
        public WinStreakStep(int trophyMultiplier, LeagueRewardPackage reward = null)
        {
            if (trophyMultiplier < 1) throw new ArgumentOutOfRangeException(nameof(trophyMultiplier), "Hệ số cúp phải >= 1.");
            TrophyMultiplier = trophyMultiplier;
            Reward = reward ?? LeagueRewardPackage.None;
        }

        public int TrophyMultiplier { get; }
        public LeagueRewardPackage Reward { get; }
    }

    /// <summary>
    /// Thang streak. Level 0 (chưa streak) luôn nhân 1; level k (1..<see cref="MaxLevel"/>) dùng bậc thứ k.
    /// Kệ 5 cúp trong design = thang 5 bậc.
    /// </summary>
    public sealed class WinStreakLadder
    {
        private readonly WinStreakStep[] _steps;

        public WinStreakLadder(IEnumerable<WinStreakStep> steps)
        {
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            var list = new List<WinStreakStep>();
            foreach (WinStreakStep step in steps)
            {
                if (step == null) throw new ArgumentException("Thang streak chứa bậc null.", nameof(steps));
                list.Add(step);
            }
            _steps = list.ToArray();
        }

        public IReadOnlyList<WinStreakStep> Steps => _steps;
        public int MaxLevel => _steps.Length;

        public int ClampLevel(int level)
        {
            if (level < 0) return 0;
            return level > MaxLevel ? MaxLevel : level;
        }

        public int MultiplierAt(int level)
        {
            int clamped = ClampLevel(level);
            return clamped == 0 ? 1 : _steps[clamped - 1].TrophyMultiplier;
        }

        /// <summary>Quà khi chạm level (1..MaxLevel); level 0 hoặc ngoài thang trả <see cref="LeagueRewardPackage.None"/>.</summary>
        public LeagueRewardPackage RewardAt(int level)
        {
            return level >= 1 && level <= MaxLevel ? _steps[level - 1].Reward : LeagueRewardPackage.None;
        }
    }
}
