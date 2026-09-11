using System;

namespace DreamTech.Leaderboard
{
    /// <summary>Một đoạn hạng liên tục [Offset, Offset + Count).</summary>
    public readonly struct RankRange
    {
        public RankRange(int offset, int count)
        {
            Offset = offset < 0 ? 0 : offset;
            Count = count < 0 ? 0 : count;
        }

        public int Offset { get; }
        public int Count { get; }
        public bool IsEmpty => Count <= 0;
        public int EndExclusive => Offset + Count;
    }

    /// <summary>Tham số tải dữ liệu của một board.</summary>
    public readonly struct FetchWindowSettings
    {
        public const int DefaultTopCount = 50;
        public const int DefaultRowsAbove = 4;
        public const int DefaultRowsBelow = 6;
        public const int DefaultMaxAnimatedPasses = 15;

        public FetchWindowSettings(int topCount, int rowsAbove, int rowsBelow, int maxAnimatedPasses)
        {
            TopCount = Math.Max(0, topCount);
            RowsAbove = Math.Max(0, rowsAbove);
            RowsBelow = Math.Max(0, rowsBelow);
            MaxAnimatedPasses = Math.Max(0, maxAnimatedPasses);
        }

        public static FetchWindowSettings Default =>
            new FetchWindowSettings(DefaultTopCount, DefaultRowsAbove, DefaultRowsBelow, DefaultMaxAnimatedPasses);

        /// <summary>Số hạng đầu luôn tải (kiểu giải đấu nhóm ~50 người thì đặt 50).</summary>
        public int TopCount { get; }

        /// <summary>Số dòng phía trên hạng người chơi.</summary>
        public int RowsAbove { get; }

        /// <summary>Số dòng phía dưới hạng người chơi.</summary>
        public int RowsBelow { get; }

        /// <summary>Số người tối đa được diễn "vượt từng người". Nhảy nhiều hơn thì quay số trước.</summary>
        public int MaxAnimatedPasses { get; }
    }

    /// <summary>Kết quả lập kế hoạch tải: đoạn top, đoạn quanh người chơi và số người sẽ diễn vượt.</summary>
    public readonly struct FetchPlan
    {
        public FetchPlan(RankRange top, RankRange window, int animatedPasses, bool isCompressed)
        {
            Top = top;
            Window = window;
            AnimatedPasses = animatedPasses;
            IsCompressed = isCompressed;
        }

        public RankRange Top { get; }
        public RankRange Window { get; }

        /// <summary>Số người được diễn vượt từng người (tối đa MaxAnimatedPasses).</summary>
        public int AnimatedPasses { get; }

        /// <summary>Nhảy hạng lớn hơn số diễn được: phải quay số trước khi leo.</summary>
        public bool IsCompressed { get; }
    }

    /// <summary>
    /// Chỉ tải đúng đoạn cần diễn: [hạng mới - vài dòng, hạng mới + số người diễn vượt (+ vài dòng)].
    /// Công thức giữ nguyên bản tham khảo; bỏ đoạn window khi nó đã nằm gọn trong top.
    /// </summary>
    public static class FetchWindowPlanner
    {
        public static FetchPlan Plan(in RankChange change, in FetchWindowSettings settings)
        {
            var top = new RankRange(0, settings.TopCount);
            if (!change.HasLocalEntry) return new FetchPlan(top, new RankRange(0, 0), 0, false);

            int toRank = change.ToRank;
            int passedTotal = change.PassedCount;
            int animated = Math.Min(passedTotal, settings.MaxAnimatedPasses);
            bool compressed = passedTotal > animated;

            int windowStart = Math.Max(0, toRank - settings.RowsAbove);
            int windowEnd = passedTotal > 0
                ? toRank + animated + (compressed ? 0 : settings.RowsBelow)
                : toRank + settings.RowsBelow;

            var window = new RankRange(windowStart, windowEnd - windowStart + 1);
            if (window.EndExclusive <= top.EndExclusive) window = new RankRange(windowStart, 0);
            return new FetchPlan(top, window, animated, compressed);
        }
    }
}
