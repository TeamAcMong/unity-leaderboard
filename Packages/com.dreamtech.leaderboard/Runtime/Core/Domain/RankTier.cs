namespace DreamTech.Leaderboard
{
    /// <summary>Tầng phần thưởng của một hạng. Quyết định mức ăn mừng: chỉ FirstPlace/Podium mới có confetti, banner, fanfare.</summary>
    public enum RankTier
    {
        FirstPlace = 0,
        Podium = 1,
        Standard = 2,
    }

    /// <summary>
    /// Luật chia tầng theo hạng. Gộp 3 cách mã hoá tier rời rạc của bản tham khảo (TierOf, badge tier, clamp feedback)
    /// về một chỗ duy nhất.
    /// </summary>
    public readonly struct RankTierRule
    {
        public const int DefaultPodiumSize = 3;

        public RankTierRule(int podiumSize)
        {
            PodiumSize = podiumSize < 1 ? 1 : podiumSize;
        }

        public static RankTierRule Default => new RankTierRule(DefaultPodiumSize);

        /// <summary>Số hạng đầu được coi là "bục vinh quang" (gồm cả hạng nhất). Giá trị default(struct) = 0 được hiểu là mặc định.</summary>
        public int PodiumSize { get; }

        private int EffectivePodiumSize => PodiumSize < 1 ? DefaultPodiumSize : PodiumSize;

        public RankTier Classify(int rank)
        {
            if (rank == 0) return RankTier.FirstPlace;
            if (rank > 0 && rank < EffectivePodiumSize) return RankTier.Podium;
            return RankTier.Standard;
        }

        /// <summary>Chỉ số huy chương (0 vàng, 1 bạc, 2 đồng...) cho badge; -1 nếu không có huy chương.</summary>
        public int MedalIndex(int rank)
        {
            return rank >= 0 && rank < EffectivePodiumSize ? rank : -1;
        }
    }
}
