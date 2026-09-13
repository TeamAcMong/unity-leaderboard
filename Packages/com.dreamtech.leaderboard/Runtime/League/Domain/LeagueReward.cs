using System;
using System.Collections.Generic;

namespace DreamTech.Leaderboard.League
{
    /// <summary>
    /// Một món quà. Id là chuỗi do game đặt (vd "coin", "booster.wiper", "heart.minutes") — package không biết kho đồ của game,
    /// adapter <see cref="ILeagueRewardGranter"/> của game dịch id sang item thật.
    /// </summary>
    public readonly struct LeagueRewardItem
    {
        public LeagueRewardItem(string itemId, int amount)
        {
            if (string.IsNullOrEmpty(itemId)) throw new ArgumentException("Item id không được rỗng.", nameof(itemId));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Số lượng quà phải dương.");
            ItemId = itemId;
            Amount = amount;
        }

        public string ItemId { get; }
        public int Amount { get; }

        public override string ToString()
        {
            return ItemId + " x" + Amount;
        }
    }

    /// <summary>Một gói quà (thường là một rương). <see cref="ChestId"/> rỗng = không có hình rương (vd quà streak).</summary>
    public sealed class LeagueRewardPackage
    {
        public static readonly LeagueRewardPackage None = new LeagueRewardPackage(string.Empty, Array.Empty<LeagueRewardItem>());

        private readonly LeagueRewardItem[] _items;

        public LeagueRewardPackage(string chestId, IEnumerable<LeagueRewardItem> items)
        {
            ChestId = chestId ?? string.Empty;
            _items = items != null ? new List<LeagueRewardItem>(items).ToArray() : Array.Empty<LeagueRewardItem>();
        }

        public string ChestId { get; }
        public IReadOnlyList<LeagueRewardItem> Items => _items;

        /// <summary>Không có món nào để phát. Có thể vẫn có <see cref="ChestId"/> (rương trống chỉ để hiển thị).</summary>
        public bool IsEmpty => _items.Length == 0;

        public override string ToString()
        {
            return (ChestId.Length > 0 ? ChestId + ": " : string.Empty) + string.Join(", ", _items);
        }
    }
}
