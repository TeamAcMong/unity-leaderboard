using System;

namespace DreamTech.Leaderboard.League
{
    /// <summary>Giờ hiện tại (UTC). Cắm giờ server để chống chỉnh giờ máy; cắm đồng hồ có offset để cheat tua giờ.</summary>
    public interface ILeagueClock
    {
        DateTime UtcNow { get; }
    }

    /// <summary>
    /// Nơi lưu chuỗi theo khoá (PlayerPrefs, save system của game, cloud...). Package tự mã hoá trạng thái thành chuỗi nên
    /// game chỉ cần cài 3 hàm này.
    /// </summary>
    public interface ILeagueTextStore
    {
        bool TryRead(string key, out string value);
        void Write(string key, string value);
        void Delete(string key);
    }

    /// <summary>
    /// Phát quà vào kho đồ của game. Trả false khi game chưa phát được lúc này (hệ thống item chưa sẵn sàng) — quà được giữ
    /// lại và phát ở lần gọi <see cref="LeagueSystem.GrantPendingRewards"/> sau. <paramref name="grantId"/> ổn định, game dùng
    /// để chống phát trùng nếu kho đồ có hỗ trợ.
    /// </summary>
    public interface ILeagueRewardGranter
    {
        bool TryGrant(string grantId, LeagueRewardPackage package);
    }

    /// <summary>Tính năng League đã mở với người chơi chưa (theo level, remote config, cheat...). Đang khoá thì không cộng cúp, không đổi streak.</summary>
    public interface ILeagueFeatureGate
    {
        bool IsUnlocked { get; }
    }
}
