namespace DreamTech.Leaderboard.League
{
    /// <summary>Vùng của một hạng trong nhóm khi mùa kết thúc.</summary>
    public enum LeagueZone
    {
        Safe = 0,
        Promotion = 1,
        Demotion = 2,
    }

    /// <summary>
    /// Ranh giới vùng lên/xuống của một nhóm. UI dùng <see cref="PromotionEndRank"/> / <see cref="DemotionStartRank"/> để chèn
    /// dải "Promotion Zone" / "Demotion Zone"; luật kết thúc mùa dùng <see cref="ZoneOf"/>. Hai chỗ đọc cùng một struct nên
    /// dải trên màn hình và kết quả thật không thể lệch nhau.
    /// </summary>
    public readonly struct LeagueZoneBands
    {
        /// <summary>Nhóm quá nhỏ để chứa cả hai vùng thì vùng lên hạng được giữ, vùng xuống hạng bị thu hẹp.</summary>
        public LeagueZoneBands(int groupSize, int promotionCount, int demotionCount)
        {
            GroupSize = groupSize < 0 ? 0 : groupSize;
            PromotionCount = Clamp(promotionCount, 0, GroupSize);
            DemotionCount = Clamp(demotionCount, 0, GroupSize - PromotionCount);
        }

        public int GroupSize { get; }
        public int PromotionCount { get; }
        public int DemotionCount { get; }

        public bool HasPromotion => PromotionCount > 0;
        public bool HasDemotion => DemotionCount > 0;

        /// <summary>Hạng 0-based đầu tiên KHÔNG còn được lên hạng (dải Promotion nằm ngay trước hạng này).</summary>
        public int PromotionEndRank => PromotionCount;

        /// <summary>Hạng 0-based đầu tiên bị xuống hạng (dải Demotion nằm ngay trước hạng này).</summary>
        public int DemotionStartRank => GroupSize - DemotionCount;

        public LeagueZone ZoneOf(int rank)
        {
            if (rank < 0 || rank >= GroupSize) return LeagueZone.Safe;
            if (rank < PromotionEndRank) return LeagueZone.Promotion;
            if (HasDemotion && rank >= DemotionStartRank) return LeagueZone.Demotion;
            return LeagueZone.Safe;
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (maximum < minimum) maximum = minimum;
            if (value < minimum) return minimum;
            return value > maximum ? maximum : value;
        }
    }
}
