using UnityEngine;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>
    /// Một dải chèn vào list ngay TRƯỚC hạng <see cref="BeforeRank"/> (0-based): "Promotion zone" đặt trước hạng đầu tiên
    /// không được lên hạng, "Demotion zone" đặt trước hạng đầu tiên bị xuống hạng...
    ///
    /// <para><see cref="View"/> là con của Content của <see cref="LeaderboardScrollView"/>; list tự đặt neo trên, chiều cao,
    /// vị trí và bật/tắt. Nội dung dải (ảnh, chữ, mũi tên) hoàn toàn của game.</para>
    /// </summary>
    public readonly struct LeaderboardRankDivider
    {
        public LeaderboardRankDivider(int beforeRank, float height, RectTransform view)
        {
            BeforeRank = beforeRank;
            Height = height;
            View = view;
        }

        public int BeforeRank { get; }
        public float Height { get; }
        public RectTransform View { get; }
    }
}
