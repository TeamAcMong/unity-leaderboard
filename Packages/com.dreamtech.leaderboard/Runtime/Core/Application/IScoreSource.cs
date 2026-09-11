using System;

namespace DreamTech.Leaderboard
{
    /// <summary>
    /// Wrapper chỉ số xếp hạng: level đã qua, tổng sao, điểm... Mỗi game tự cắm một cài đặt, board chỉ hỏi "điểm bây giờ là bao nhiêu".
    /// Nhờ vậy nơi gọi (màn Win, cheat, Home) không bao giờ phải biết chỉ số là gì.
    /// </summary>
    public interface IScoreSource
    {
        /// <summary>Tên chỉ số, dùng để log/debug (vd "levels-completed").</summary>
        string MetricId { get; }

        /// <summary>False khi chưa có điểm có nghĩa (vd chưa qua màn nào) — board sẽ không submit.</summary>
        bool TryGetScore(out long score);

        /// <summary>Báo điểm vừa đổi, để nơi lắp ráp quyết định có sync ngay không.</summary>
        event Action ScoreChanged;
    }
}
