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
            : this(board, mode, hostReady, skipIntro: false)
        {
        }

        /// <param name="skipIntro">
        /// true = các row hiện NGAY ở chỗ của chúng, không trượt vào (xem <see cref="SkipIntro"/>). false = như constructor
        /// ba tham số.
        /// </param>
        public LeaderboardPresentRequest(ILeaderboardBoard board, BoardPresentMode mode, Task hostReady, bool skipIntro)
        {
            Board = board;
            Mode = mode;
            HostReady = hostReady;
            SkipIntro = skipIntro;
        }

        public ILeaderboardBoard Board { get; }
        public BoardPresentMode Mode { get; }
        public Task HostReady { get; }

        /// <summary>
        /// Bỏ đợt trượt vào của các row: danh sách dựng thẳng ở tư thế đứng (camera vẫn đặt vào row người chơi), màn diễn
        /// (nếu có) bắt đầu từ đó. false (mặc định) = như cũ.
        ///
        /// <para>Dùng cho lượt trình bày LẠI trên một trang đã mở sẵn — ví dụ host cộng thêm điểm rồi diễn tiếp cú leo: bảng
        /// đang đứng trên màn, cho nó trượt vào lần nữa là diễn lại một nhịp người chơi vừa xem xong. Đặt tên theo chiều
        /// "bỏ" để giá trị mặc định của struct (<c>default</c>) vẫn là hành vi cũ.</para>
        /// </summary>
        public bool SkipIntro { get; }
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
