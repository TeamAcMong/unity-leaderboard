using System;
using System.Threading.Tasks;
using DreamTech.Leaderboard.ViewModel;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>Yêu cầu trình bày: board nào, chế độ gì, và khi nào host (popup/màn hình) sẵn sàng để bắt đầu intro.</summary>
    public readonly struct LeaderboardPresentRequest
    {
        /// <param name="hostReady">Hoàn tất khi animation mở của host xong (null = sẵn sàng ngay). Widget tải dữ liệu song song và
        /// chỉ bắt đầu intro khi task này xong hoặc hết timeout.</param>
        public LeaderboardPresentRequest(ILeaderboardBoard board, BoardPresentMode mode, Task hostReady = null)
        {
            Board = board;
            Mode = mode;
            HostReady = hostReady;
        }

        public ILeaderboardBoard Board { get; }
        public BoardPresentMode Mode { get; }
        public Task HostReady { get; }
    }

    public enum PresentOutcome
    {
        /// <summary>Hiện xong (và diễn xong nếu có).</summary>
        Completed = 0,

        /// <summary>Người chơi chạm để skip; nhịp hạ cánh vẫn đã diễn.</summary>
        Skipped = 1,

        /// <summary>Host đóng hoặc trình bày mới thay thế trước khi xong.</summary>
        Cancelled = 2,

        /// <summary>Tải dữ liệu hoặc diễn bị lỗi; widget đang hiện trạng thái lỗi + Retry.</summary>
        Failed = 3,
    }

    public readonly struct LeaderboardPresentResult
    {
        public LeaderboardPresentResult(PresentOutcome outcome, RankChange change, Exception error)
        {
            Outcome = outcome;
            Change = change;
            Error = error;
        }

        public PresentOutcome Outcome { get; }
        public RankChange Change { get; }
        public Exception Error { get; }
    }

    /// <summary>Những thứ mọi view cần để render một khung hình; widget tạo một lần cho mỗi lần trình bày.</summary>
    public sealed class LeaderboardRenderContext
    {
        public LeaderboardRenderContext(MotionSettings motion, LeaderboardVisualSettings visuals, LeaderboardThemeConfig theme,
                                        LeaderboardTextConfig text, RankTierRule tierRule)
        {
            Motion = motion ?? throw new ArgumentNullException(nameof(motion));
            Visuals = visuals ?? throw new ArgumentNullException(nameof(visuals));
            Theme = theme ? theme : throw new ArgumentNullException(nameof(theme));
            Text = text ? text : throw new ArgumentNullException(nameof(text));
            TierRule = tierRule;
        }

        public MotionSettings Motion { get; }
        public LeaderboardVisualSettings Visuals { get; }
        public LeaderboardThemeConfig Theme { get; }
        public LeaderboardTextConfig Text { get; }
        public RankTierRule TierRule { get; }

        /// <summary>Đồng hồ của model ở frame hiện tại.</summary>
        public double Clock { get; internal set; }

        /// <summary>Delta time đã nhân time-scale debug.</summary>
        public float DeltaTime { get; internal set; }
    }
}
