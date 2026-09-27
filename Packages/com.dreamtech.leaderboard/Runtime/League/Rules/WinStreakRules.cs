using System;

namespace DreamTech.Leaderboard.League
{
    /// <summary>
    /// Luật streak. Mọi nơi hiển thị cảnh báo (popup thoát, popup thua) hỏi <see cref="WouldReset"/> thay vì tự đoán, nên đổi
    /// luật chỉ là cắm class khác.
    /// </summary>
    public interface IWinStreakRule
    {
        WinStreakState Apply(WinStreakState state, WinStreakEvent streakEvent, WinStreakLadder ladder);

        /// <summary>Sự kiện này có làm mất streak đang có không (false nếu đang không có streak).</summary>
        bool WouldReset(WinStreakState state, WinStreakEvent streakEvent);
    }

    /// <summary>
    /// Mặc định: thắng đủ <see cref="WinsPerLevel"/> trận thì lên một bậc (kẹt ở bậc cao nhất). Thua hẳn, thoát giữa chừng,
    /// chơi lại giữa chừng thì mất hết. Hồi sinh thì giữ (tắt được bằng <see cref="ReviveKeepsStreak"/>). Thoát giữa chừng
    /// thì mất, trừ khi bật <see cref="QuitKeepsStreak"/> (constructor ba tham số).
    /// </summary>
    public sealed class StandardWinStreakRule : IWinStreakRule
    {
        public StandardWinStreakRule(int winsPerLevel = 1, bool reviveKeepsStreak = true)
        {
            if (winsPerLevel < 1) throw new ArgumentOutOfRangeException(nameof(winsPerLevel), "Cần ít nhất 1 trận thắng mỗi bậc.");
            WinsPerLevel = winsPerLevel;
            ReviveKeepsStreak = reviveKeepsStreak;
        }

        /// <summary>
        /// Như constructor hai tham số, thêm <paramref name="quitKeepsStreak"/>: true thì thoát giữa chừng
        /// (<see cref="WinStreakEvent.LevelQuit"/>) GIỮ streak; chơi lại và thua hẳn vẫn mất.
        /// </summary>
        public StandardWinStreakRule(int winsPerLevel, bool reviveKeepsStreak, bool quitKeepsStreak)
            : this(winsPerLevel, reviveKeepsStreak)
        {
            QuitKeepsStreak = quitKeepsStreak;
        }

        public int WinsPerLevel { get; }
        public bool ReviveKeepsStreak { get; }

        /// <summary>Thoát giữa chừng có giữ streak không. Mặc định false (thoát là mất, như trước khi có cờ này).</summary>
        public bool QuitKeepsStreak { get; }

        public WinStreakState Apply(WinStreakState state, WinStreakEvent streakEvent, WinStreakLadder ladder)
        {
            if (ladder == null) throw new ArgumentNullException(nameof(ladder));
            if (streakEvent != WinStreakEvent.LevelWon)
            {
                return ResetsOn(streakEvent) ? WinStreakState.Empty : state;
            }

            int level = ladder.ClampLevel(state.Level);
            if (level >= ladder.MaxLevel) return new WinStreakState(ladder.MaxLevel, 0);

            int wins = state.WinsTowardNextLevel + 1;
            return wins >= WinsPerLevel ? new WinStreakState(level + 1, 0) : new WinStreakState(level, wins);
        }

        public bool WouldReset(WinStreakState state, WinStreakEvent streakEvent)
        {
            return !state.IsEmpty && ResetsOn(streakEvent);
        }

        private bool ResetsOn(WinStreakEvent streakEvent)
        {
            switch (streakEvent)
            {
                case WinStreakEvent.LevelLost:
                case WinStreakEvent.LevelRetried:
                    return true;
                case WinStreakEvent.LevelQuit:
                    return !QuitKeepsStreak;
                case WinStreakEvent.LevelRevived:
                    return !ReviveKeepsStreak;
                default:
                    return false;
            }
        }
    }
}
