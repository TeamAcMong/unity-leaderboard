using System;

namespace DreamTech.Leaderboard.League
{
    /// <summary>Vì sao dịch vụ nhóm từ chối một lần cộng cúp.</summary>
    public enum LeagueTrophyGrantRejection
    {
        /// <summary>
        /// Mùa của grant đã khép và kết quả đã được chốt với người chơi (đã xem, hoặc đã nhận rương). Cộng thêm lúc này sẽ làm kết
        /// quả vừa hiện (hạng, rương, bậc mùa sau) sai đi, nên dịch vụ giữ nguyên dữ liệu và từ chối.
        /// </summary>
        SeasonAlreadyFinalized = 1,

        /// <summary>
        /// Dịch vụ không nhận mùa này:
        /// <list type="bullet">
        /// <item>mùa cũ hơn mùa đầu tiên dịch vụ từng giữ (người chơi chưa vào nhóm ở mùa đó), hoặc cũ hơn mùa khép gần nhất mà
        /// không có sổ;</item>
        /// <item>quá cũ nên sổ của mùa đó không còn lưu;</item>
        /// <item>mùa chưa kết thúc trước mùa hiện tại mà cũng không phải mùa dịch vụ đang giữ (đồng hồ lùi, lịch mùa đổi làm cửa sổ
        /// chồng nhau);</item>
        /// <item>mùa bị "nhảy qua" nhưng grant không mang cửa sổ mùa (tạo bằng constructor chỉ có id mùa) nên dịch vụ không biết mùa
        /// đó nằm ở đâu trên dòng thời gian.</item>
        /// </list>
        /// Grant do <see cref="LeagueSystem"/> tạo luôn mang cửa sổ mùa, nên mùa bị nhảy qua vẫn được tính; với đồng hồ không lùi
        /// (<see cref="MonotonicLeagueClock"/>) lý do này gần như chỉ còn gặp khi dữ liệu dịch vụ bị xoá mà hàng chờ cúp thì không.
        /// </summary>
        UnknownSeason = 2,
    }

    /// <summary>
    /// <see cref="ILeagueGroupService.AddTrophiesAsync"/> từ chối DỨT KHOÁT một grant (khác lỗi mạng: gửi lại bao nhiêu lần cũng
    /// vậy). <see cref="LeagueSystem"/> bắt riêng loại này: bỏ grant khỏi hàng chờ để không chặn các grant phía sau, và báo qua
    /// <see cref="LeagueSystem.TrophyGrantRejected"/> — cúp không bao giờ biến mất im lặng.
    ///
    /// <para>Gửi lại một grant ĐÃ được tính (trùng <see cref="LeagueTrophyGrant.GrantId"/>) không phải là bị từ chối: dịch vụ trả
    /// về bình thường, không ném lỗi này.</para>
    /// </summary>
    public sealed class LeagueTrophyGrantRejectedException : Exception
    {
        public LeagueTrophyGrantRejectedException(LeagueTrophyGrant grant, LeagueTrophyGrantRejection reason, string message)
            : base(message)
        {
            Grant = grant;
            Reason = reason;
        }

        public LeagueTrophyGrant Grant { get; }
        public LeagueTrophyGrantRejection Reason { get; }
    }
}
