using System.Threading;
using System.Threading.Tasks;

namespace DreamTech.Leaderboard.League
{
    /// <summary>
    /// "Module xử lý" của League: ghép nhóm, bảng xếp hạng, cộng cúp, khép mùa, nhận thưởng. Đây là chỗ cắm-rút chính:
    /// bản mô phỏng bot (<see cref="SimulatedLeagueGroupService"/>) và backend thật cùng cài port này, phần còn lại
    /// (luật, streak, UI, lưu trạng thái) không đổi khi thay.
    ///
    /// <para>Hợp đồng mọi bản cài phải giữ:
    /// <list type="bullet">
    /// <item>Standings sort giảm dần theo cúp, rank 0-based liên tục.</item>
    /// <item><see cref="AddTrophiesAsync"/> idempotent theo <see cref="LeagueTrophyGrant.GrantId"/>; grant của mùa vừa hết nhưng
    /// chưa khép vẫn được tính cho mùa đó.</item>
    /// <item><see cref="ClaimSeasonRewardAsync"/> trả gói quà đúng một lần, các lần sau trả <see cref="LeagueRewardPackage.None"/>.</item>
    /// <item>Lỗi mạng ném exception (không trả null).</item>
    /// </list></para>
    /// </summary>
    public interface ILeagueGroupService
    {
        string LocalPlayerId { get; }

        /// <summary>Nhóm của người chơi trong mùa <paramref name="currentSeason"/>; tự vào nhóm nếu chưa có.</summary>
        Task<LeagueGroupSnapshot> GetGroupAsync(SeasonWindow currentSeason, CancellationToken cancellationToken);

        Task<LeagueGroupSnapshot> AddTrophiesAsync(SeasonWindow currentSeason, LeagueTrophyGrant grant, CancellationToken cancellationToken);

        /// <summary>Kết quả mùa cũ nhất còn việc cho UI (<see cref="SeasonResult.IsPending"/>); null nếu không có.</summary>
        Task<SeasonResult> GetPendingResultAsync(SeasonWindow currentSeason, CancellationToken cancellationToken);

        Task AcknowledgeResultAsync(string seasonId, CancellationToken cancellationToken);

        Task<LeagueRewardPackage> ClaimSeasonRewardAsync(string seasonId, CancellationToken cancellationToken);
    }
}
