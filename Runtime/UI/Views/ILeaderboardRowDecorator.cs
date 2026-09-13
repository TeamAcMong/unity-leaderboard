using DreamTech.Leaderboard.ViewModel;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>
    /// Chỗ cắm nội dung riêng của game vào row mà không phải sửa <see cref="LeaderboardEntryView"/>: rương thưởng theo hạng của
    /// League, cờ quốc gia, khung avatar VIP... Gắn component cài interface này lên prefab row (hoặc con của nó); row gọi
    /// <see cref="OnRowBound"/> mỗi lần nội dung đổi — kể cả khi hạng đang hiện đổi giữa animation leo hạng.
    ///
    /// <para>Chỉ đổi nội dung tĩnh ở đây (sprite, chữ, bật/tắt). Hiệu ứng theo thời gian vẫn thuộc về <see cref="RowState"/>
    /// để hai view render cùng một row luôn trông giống hệt nhau.</para>
    /// </summary>
    public interface ILeaderboardRowDecorator
    {
        /// <param name="row">Row vừa bind; <see cref="RowState.IsGap"/> = true với row "..." — decorator tự ẩn phần của mình.</param>
        void OnRowBound(RowState row, LeaderboardRenderContext context);
    }
}
