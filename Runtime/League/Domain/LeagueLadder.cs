using System;
using System.Collections.Generic;

namespace DreamTech.Leaderboard.League
{
    /// <summary>Một bậc league (Bronze, Silver...). Chỉ chứa luật; màu, cúp, banner là việc của UI.</summary>
    public sealed class LeagueTierDefinition
    {
        public LeagueTierDefinition(string tierId, int promotionCount, int demotionCount)
        {
            if (string.IsNullOrEmpty(tierId)) throw new ArgumentException("Tier id không được rỗng.", nameof(tierId));
            if (promotionCount < 0) throw new ArgumentOutOfRangeException(nameof(promotionCount), "Số người lên hạng không được âm.");
            if (demotionCount < 0) throw new ArgumentOutOfRangeException(nameof(demotionCount), "Số người xuống hạng không được âm.");
            TierId = tierId;
            PromotionCount = promotionCount;
            DemotionCount = demotionCount;
        }

        public string TierId { get; }

        /// <summary>Số người đứng đầu nhóm được lên bậc trên. Bậc cao nhất bỏ qua giá trị này.</summary>
        public int PromotionCount { get; }

        /// <summary>Số người đứng cuối nhóm bị xuống bậc dưới. Bậc thấp nhất bỏ qua giá trị này.</summary>
        public int DemotionCount { get; }
    }

    /// <summary>Thang league xếp từ thấp (index 0) lên cao. Bất biến sau khi tạo.</summary>
    public sealed class LeagueLadder
    {
        private readonly LeagueTierDefinition[] _tiers;

        public LeagueLadder(IEnumerable<LeagueTierDefinition> tiers)
        {
            if (tiers == null) throw new ArgumentNullException(nameof(tiers));
            var list = new List<LeagueTierDefinition>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (LeagueTierDefinition tier in tiers)
            {
                if (tier == null) throw new ArgumentException("Thang league chứa tier null.", nameof(tiers));
                if (!seenIds.Add(tier.TierId)) throw new ArgumentException("Tier id bị trùng: " + tier.TierId, nameof(tiers));
                list.Add(tier);
            }
            if (list.Count == 0) throw new ArgumentException("Thang league phải có ít nhất một tier.", nameof(tiers));
            _tiers = list.ToArray();
        }

        public IReadOnlyList<LeagueTierDefinition> Tiers => _tiers;
        public int Count => _tiers.Length;
        public int TopIndex => _tiers.Length - 1;

        public LeagueTierDefinition TierAt(int tierIndex)
        {
            if (tierIndex < 0 || tierIndex >= _tiers.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(tierIndex), "Tier index ngoài thang: " + tierIndex);
            }
            return _tiers[tierIndex];
        }

        /// <summary>-1 nếu không có tier này.</summary>
        public int IndexOf(string tierId)
        {
            if (tierId == null) return -1;
            for (int index = 0; index < _tiers.Length; index++)
            {
                if (string.Equals(_tiers[index].TierId, tierId, StringComparison.Ordinal)) return index;
            }
            return -1;
        }

        public int ClampIndex(int tierIndex)
        {
            if (tierIndex < 0) return 0;
            return tierIndex > TopIndex ? TopIndex : tierIndex;
        }

        public bool IsBottom(int tierIndex)
        {
            return tierIndex <= 0;
        }

        public bool IsTop(int tierIndex)
        {
            return tierIndex >= TopIndex;
        }
    }
}
