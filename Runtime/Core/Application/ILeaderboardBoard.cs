using System;
using System.Threading;
using System.Threading.Tasks;

namespace DreamTech.Leaderboard
{
    /// <summary>
    /// Một bảng xếp hạng đã được lắp ráp (backend + chỉ số + nơi lưu). Là thứ duy nhất widget cần; widget không tự tìm board
    /// mà được host truyền vào.
    /// </summary>
    public interface ILeaderboardBoard
    {
        string BoardId { get; }
        RankTierRule TierRule { get; }

        /// <summary>
        /// Có thay đổi đáng diễn chưa được xem không (lên hạng, điểm tốt hơn, hoặc điểm nguồn cao hơn điểm đã submit).
        /// Không gọi mạng — dựa trên dữ liệu đã biết từ lần sync gần nhất, đủ rẻ để Home hỏi mỗi lần hiện.
        /// </summary>
        bool HasUnrevealedChange { get; }

        /// <summary>Kéo điểm từ nguồn; nếu tốt hơn điểm trên backend thì submit. Lỗi thì lần sync sau tự thử lại.</summary>
        Task<LeaderboardEntry> SyncScoreAsync(CancellationToken cancellationToken);

        /// <summary>Sync rồi tải đúng đoạn dữ liệu cần cho chế độ trình bày.</summary>
        Task<BoardScene> LoadSceneAsync(BoardPresentMode mode, CancellationToken cancellationToken);

        /// <summary>
        /// Ghi nhận người chơi đã xem thay đổi này (gọi khi màn diễn chạm tới nhịp hạ cánh, kể cả khi skip). Snapshot thuộc mùa của
        /// dữ liệu đã tính ra thay đổi (<see cref="RankChange.SeasonKey"/>), không phải mùa lúc gọi.
        /// </summary>
        void MarkRevealed(in RankChange change);

        /// <summary>Mỗi board chỉ một màn reveal tại một thời điểm. Dispose lease để nhả.</summary>
        bool TryBeginReveal(out IDisposable lease);
    }
}
