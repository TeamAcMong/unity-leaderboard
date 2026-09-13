using System;
using System.Collections.Generic;

namespace DreamTech.Leaderboard.League
{
    /// <summary>Luật số cúp mỗi lần thắng level.</summary>
    public interface ITrophyRule
    {
        int TrophiesForWin(in LevelWinContext context, WinStreakState streakBeforeWin, WinStreakState streakAfterWin, WinStreakLadder ladder);
    }

    /// <summary>
    /// Mặc định: cúp gốc theo độ khó × hệ số streak. Độ khó vượt bảng dùng giá trị cuối bảng.
    /// Hệ số lấy theo streak TRƯỚC trận thắng (streak mang vào level) trừ khi bật <see cref="MultiplierFromStreakAfterWin"/>.
    /// </summary>
    public sealed class MultipliedTrophyRule : ITrophyRule
    {
        private readonly int[] _baseTrophiesByDifficulty;

        public MultipliedTrophyRule(IEnumerable<int> baseTrophiesByDifficulty, bool multiplierFromStreakAfterWin = false)
        {
            if (baseTrophiesByDifficulty == null) throw new ArgumentNullException(nameof(baseTrophiesByDifficulty));
            var values = new List<int>(baseTrophiesByDifficulty);
            if (values.Count == 0) throw new ArgumentException("Cần ít nhất một mức cúp gốc.", nameof(baseTrophiesByDifficulty));
            foreach (int value in values)
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(baseTrophiesByDifficulty), "Cúp gốc không được âm.");
            }
            _baseTrophiesByDifficulty = values.ToArray();
            MultiplierFromStreakAfterWin = multiplierFromStreakAfterWin;
        }

        public IReadOnlyList<int> BaseTrophiesByDifficulty => _baseTrophiesByDifficulty;
        public bool MultiplierFromStreakAfterWin { get; }

        public int TrophiesForWin(in LevelWinContext context, WinStreakState streakBeforeWin, WinStreakState streakAfterWin, WinStreakLadder ladder)
        {
            if (ladder == null) throw new ArgumentNullException(nameof(ladder));
            int difficulty = Math.Min(context.DifficultyIndex, _baseTrophiesByDifficulty.Length - 1);
            int baseTrophies = _baseTrophiesByDifficulty[difficulty];
            WinStreakState streak = MultiplierFromStreakAfterWin ? streakAfterWin : streakBeforeWin;
            return checked(baseTrophies * ladder.MultiplierAt(streak.Level));
        }
    }
}
