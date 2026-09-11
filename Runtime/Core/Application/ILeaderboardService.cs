using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DreamTech.Leaderboard
{
    /// <summary>
    /// Port tới backend (Mock, Unity Gaming Services, PlayFab, server riêng...). UI không bao giờ biết backend là gì.
    /// Quy ước: điểm "giữ tốt nhất", sort giảm dần, rank 0-based. Dùng BCL Task để assembly thuần C# không phụ thuộc UniTask.
    /// </summary>
    public interface ILeaderboardService
    {
        string LocalPlayerId { get; }

        /// <summary>Khoá mùa giải hiện tại (vd "weekly-2026-37"). Backend không chia mùa thì trả chuỗi cố định.</summary>
        string SeasonKey { get; }

        /// <summary>Entry của người chơi, hoặc null nếu chưa từng submit.</summary>
        Task<LeaderboardEntry> GetLocalEntryAsync(CancellationToken cancellationToken);

        /// <summary>Submit điểm (giữ điểm tốt nhất), trả về entry SAU khi submit.</summary>
        Task<LeaderboardEntry> SubmitScoreAsync(long score, CancellationToken cancellationToken);

        /// <summary>Lấy đoạn [offset, offset + limit) theo rank 0-based.</summary>
        Task<IReadOnlyList<LeaderboardEntry>> GetRangeAsync(int offset, int limit, CancellationToken cancellationToken);
    }
}
