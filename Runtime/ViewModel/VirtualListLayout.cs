using System;

namespace DreamTech.Leaderboard.ViewModel
{
    /// <summary>
    /// Toán vị trí của list ảo hoá theo Slot. Trục Y hướng xuống, 0 = mép trên nội dung; scroll = khoảng nội dung đã cuộn qua.
    /// </summary>
    public readonly struct VirtualListLayout
    {
        public VirtualListLayout(float rowHeight, float spacing, float topPadding, float bottomPadding)
        {
            RowHeight = rowHeight;
            Spacing = spacing;
            TopPadding = topPadding;
            BottomPadding = bottomPadding;
        }

        public float RowHeight { get; }
        public float Spacing { get; }
        public float TopPadding { get; }
        public float BottomPadding { get; }
        public float Stride => RowHeight + Spacing;

        public float SlotToTop(float slot)
        {
            return TopPadding + slot * Stride;
        }

        public float SlotToCenter(float slot)
        {
            return SlotToTop(slot) + RowHeight * 0.5f;
        }

        /// <summary>Chiều cao nội dung đủ chứa tới slot lớn nhất; -1 (không có row) cho 0.</summary>
        public float ContentHeight(float maximumSlot)
        {
            int rowCount = (int)Math.Ceiling(maximumSlot) + 1;
            return rowCount > 0 ? TopPadding + rowCount * Stride - Spacing + BottomPadding : 0f;
        }

        public float MaximumScroll(float contentHeight, float viewportHeight)
        {
            return Math.Max(0f, contentHeight - viewportHeight);
        }

        /// <summary>Scroll để ô nằm giữa viewport, kẹp trong khoảng cuộn được.</summary>
        public float CenteredScroll(float slot, float viewportHeight, float maximumScroll)
        {
            return Easing.Clamp(SlotToCenter(slot) - viewportHeight * 0.5f, 0f, maximumScroll);
        }

        /// <summary>Ô có chạm vào dải [bandTop, bandBottom] không.</summary>
        public bool Intersects(float slot, float bandTop, float bandBottom)
        {
            float top = SlotToTop(slot);
            return top + RowHeight >= bandTop && top <= bandBottom;
        }

        public float TopVisibleSlot(float scroll)
        {
            return (scroll - TopPadding) / Stride;
        }

        public int VisibleRowCapacity(float viewportHeight)
        {
            return Stride <= 0f ? 0 : Math.Max(0, (int)Math.Floor((viewportHeight + Spacing) / Stride));
        }
    }
}
