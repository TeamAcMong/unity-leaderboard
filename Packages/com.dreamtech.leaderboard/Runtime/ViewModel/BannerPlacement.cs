namespace DreamTech.Leaderboard.ViewModel
{
    public enum BannerSide
    {
        Below = 0,
        Above = 1,
    }

    /// <summary>
    /// Đặt banner top 3 / #1 sao cho KHÔNG BAO GIỜ che row người chơi: ưu tiên ngay dưới row; không đủ chỗ thì lên trên;
    /// cả hai đều không vừa thì chọn phía rộng hơn và kẹp trong vùng chứa.
    /// Mọi toạ độ theo trục Y hướng xuống (giá trị lớn hơn = thấp hơn trên màn hình).
    /// </summary>
    public readonly struct BannerPlacement
    {
        private BannerPlacement(float centerY, BannerSide side)
        {
            CenterY = centerY;
            Side = side;
        }

        public float CenterY { get; }
        public BannerSide Side { get; }

        public static BannerPlacement Resolve(float rowTop, float rowBottom, float bannerHeight, float containerTop,
                                              float containerBottom, float gap)
        {
            float halfHeight = bannerHeight * 0.5f;

            float belowTop = rowBottom + gap;
            if (belowTop + bannerHeight <= containerBottom) return new BannerPlacement(belowTop + halfHeight, BannerSide.Below);

            float aboveBottom = rowTop - gap;
            if (aboveBottom - bannerHeight >= containerTop) return new BannerPlacement(aboveBottom - halfHeight, BannerSide.Above);

            float spaceBelow = containerBottom - rowBottom;
            float spaceAbove = rowTop - containerTop;
            float minimumCenter = containerTop + halfHeight;
            float maximumCenter = containerBottom - halfHeight;
            if (maximumCenter < minimumCenter) maximumCenter = minimumCenter;

            if (spaceBelow >= spaceAbove)
            {
                return new BannerPlacement(Easing.Clamp(belowTop + halfHeight, minimumCenter, maximumCenter), BannerSide.Below);
            }
            return new BannerPlacement(Easing.Clamp(aboveBottom - halfHeight, minimumCenter, maximumCenter), BannerSide.Above);
        }
    }
}
