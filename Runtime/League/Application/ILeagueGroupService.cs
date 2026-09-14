using System.Threading;
using System.Threading.Tasks;

namespace DreamTech.Leaderboard.League
{
    /// <summary>
    /// "Module xử lý" của League: ghép nhóm, bảng xếp hạng, cộng cúp, khép mùa, nhận thưởng. Đây là chỗ cắm-rút chính:
    /// bản mô phỏng bot (<see cref="SimulatedLeagueGroupService"/>) và backend thật cùng cài port này, phần còn lại
    /// (luật, streak, UI, lưu trạng thái) không đổi khi thay.
    ///
    /// <para>Hợp đồng mọi bản cài phải giữ (bộ test <c>LeagueGroupServiceContract</c> kiểm từng điều):
    /// <list type="bullet">
    /// <item>Standings sort giảm dần theo cúp, rank 0-based liên tục.</item>
    /// <item><see cref="AddTrophiesAsync"/> idempotent theo <see cref="LeagueTrophyGrant.GrantId"/> — kể cả khi grant được gửi lại
    /// sau khi mùa của nó đã khép và kết quả đã chốt (trả về bình thường, không cộng, không ném lỗi).</item>
    /// <item><b>Không cúp nào mất quanh lúc đổi mùa.</b> Grant của mùa vừa hết nhưng chưa khép vẫn được tính cho mùa đó; nhiều grant
    /// của mùa cũ xếp hàng sau lúc đổi mùa thì TẤT CẢ được tính cho mùa cũ. Grant đầu tiên của mùa mới trong khi dịch vụ còn giữ
    /// mùa cũ đã hết: khép mùa cũ TRƯỚC rồi cộng vào mùa mới.</item>
    /// <item><b>Grant của mùa bị "nhảy qua"</b> (dịch vụ chưa từng giữ mùa đó vì mọi lần gửi trong mùa đó thất bại; grant mang cửa
    /// sổ mùa <see cref="LeagueTrophyGrant.Season"/>, mùa đó kết thúc trước khi <c>currentSeason</c> bắt đầu): nhận khi MỌI mùa dịch
    /// vụ đã biết mà bắt đầu sau khi mùa của grant kết thúc (mùa đang giữ và các mùa đã khép) đều còn <b>trống</b> — không cúp, không
    /// grant, kết quả (nếu có) chưa xem / chưa nhận. Dịch vụ dựng một mùa đã khép cho nó, chèn đúng thứ tự thời gian (bậc = bậc mùa
    /// sau của mùa khép liền trước; không có mùa liền trước thì bậc mà mùa trống đầu tiên phía sau đã bắt đầu), cộng grant, tính
    /// kết quả, rồi tính lại theo chuỗi bậc mới các mùa trống phía sau (kết quả của chúng có thể xuất hiện / biến mất đúng luật) và
    /// bậc của mùa đang giữ nếu nó trống. <b>Kết quả không được phụ thuộc thứ tự lượt gọi:</b> dịch vụ còn giữ mùa trước grant (khép
    /// mùa đó trước), đã sang mùa trống phía sau, đã mở rồi khép mùa trống phía sau, hay mùa đầu tiên nó từng giữ là mùa trống phía
    /// sau — cùng một hàng chờ phải ra cùng dữ liệu. Nhiều mùa trống bị nhảy qua thì chỉ mùa có grant có kết quả. Có mùa phía sau
    /// KHÔNG trống (người chơi đã kiếm cúp hoặc đã chốt kết quả ở bậc tính khi chưa có grant này), cửa sổ mùa chồng lấn, hoặc grant
    /// không mang cửa sổ mùa → không nhận: ném <see cref="LeagueTrophyGrantRejectedException"/> với
    /// <see cref="LeagueTrophyGrantRejection.UnknownSeason"/>.</item>
    /// <item><b>Grant tới muộn cho mùa ĐÃ khép:</b> nếu kết quả mùa đó chưa được chốt với người chơi (chưa
    /// <see cref="AcknowledgeResultAsync"/>, chưa <see cref="ClaimSeasonRewardAsync"/>) thì cộng vào và tính lại kết quả (cúp,
    /// hạng cuối, outcome, rương; bậc mùa sau đổi thì các mùa phía sau còn trống — kể cả mùa đang giữ chưa có cúp — cũng đổi bậc
    /// và tính lại theo, dừng ở mùa đầu tiên không trống). Nếu đã chốt thì KHÔNG đổi dữ liệu và
    /// ném <see cref="LeagueTrophyGrantRejectedException"/> (<see cref="LeagueTrophyGrantRejection.SeasonAlreadyFinalized"/>);
    /// grant của mùa mà dịch vụ không nhận thì ném cùng loại lỗi với <see cref="LeagueTrophyGrantRejection.UnknownSeason"/>.</item>
    /// <item><b>Không bao giờ lùi mùa:</b> <c>currentSeason</c> cũ hơn mùa đang giữ (đồng hồ lùi) thì giữ nguyên mùa đang giữ cùng
    /// số cúp của nó; không mở lại một mùa đã khép; mỗi mùa có tối đa MỘT <see cref="SeasonResult"/>.</item>
    /// <item><see cref="AcknowledgeResultAsync"/> và <see cref="ClaimSeasonRewardAsync"/> tác động đúng kết quả đang chờ của mùa đó,
    /// gọi lại bao nhiêu lần cũng an toàn; <see cref="ClaimSeasonRewardAsync"/> trả gói quà đúng một lần, các lần sau trả
    /// <see cref="LeagueRewardPackage.None"/>. Xem xong + nhận xong thì <see cref="GetPendingResultAsync"/> không trả mùa đó nữa.</item>
    /// <item>Lỗi mạng ném exception (không trả null).</item>
    /// </list></para>
    /// </summary>
    public interface ILeagueGroupService
    {
        string LocalPlayerId { get; }

        /// <summary>Nhóm của người chơi trong mùa <paramref name="currentSeason"/>; tự vào nhóm nếu chưa có.</summary>
        Task<LeagueGroupSnapshot> GetGroupAsync(SeasonWindow currentSeason, CancellationToken cancellationToken);

        /// <summary>
        /// Cộng cúp (idempotent theo grant id), trả bảng của mùa đang chạy. Bị từ chối dứt khoát thì ném
        /// <see cref="LeagueTrophyGrantRejectedException"/> — xem hợp đồng ở đầu interface.
        /// </summary>
        Task<LeagueGroupSnapshot> AddTrophiesAsync(SeasonWindow currentSeason, LeagueTrophyGrant grant, CancellationToken cancellationToken);

        /// <summary>Kết quả mùa cũ nhất còn việc cho UI (<see cref="SeasonResult.IsPending"/>); null nếu không có.</summary>
        Task<SeasonResult> GetPendingResultAsync(SeasonWindow currentSeason, CancellationToken cancellationToken);

        Task AcknowledgeResultAsync(string seasonId, CancellationToken cancellationToken);

        Task<LeagueRewardPackage> ClaimSeasonRewardAsync(string seasonId, CancellationToken cancellationToken);
    }
}
