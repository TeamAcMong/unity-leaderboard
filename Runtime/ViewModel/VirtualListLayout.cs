using System;
using System.Collections.Generic;

namespace DreamTech.Leaderboard.ViewModel
{
    /// <summary>
    /// Một khoảng chèn giữa hai row: dải Promotion / Demotion của League, tiêu đề nhóm... Nằm ngay TRÊN row ở
    /// <see cref="BeforeSlot"/>, dính sát row đó (không cộng thêm spacing).
    /// </summary>
    public readonly struct ListDivider
    {
        public ListDivider(int beforeSlot, float height)
        {
            BeforeSlot = beforeSlot;
            Height = Math.Max(0f, height);
        }

        public int BeforeSlot { get; }
        public float Height { get; }
    }

    /// <summary>
    /// Toán vị trí của list ảo hoá theo Slot. Trục Y hướng xuống, 0 = mép trên nội dung; scroll = khoảng nội dung đã cuộn qua.
    ///
    /// <para>Có divider thì khoảng của nó được "trải" dần theo slot trong đoạn [BeforeSlot - 1, BeforeSlot]: row đang leo
    /// (slot lẻ) lướt qua dải một cách liên tục thay vì nhảy cóc một đoạn bằng chiều cao dải. Không có divider thì mọi
    /// công thức trùng khít bản cũ.</para>
    /// </summary>
    public readonly struct VirtualListLayout
    {
        private readonly IReadOnlyList<ListDivider> _dividers;

        public VirtualListLayout(float rowHeight, float spacing, float topPadding, float bottomPadding)
            : this(rowHeight, spacing, topPadding, bottomPadding, null)
        {
        }

        /// <param name="dividers">Sắp tăng dần theo <see cref="ListDivider.BeforeSlot"/>, không trùng slot.</param>
        public VirtualListLayout(float rowHeight, float spacing, float topPadding, float bottomPadding, IReadOnlyList<ListDivider> dividers)
        {
            RowHeight = rowHeight;
            Spacing = spacing;
            TopPadding = topPadding;
            BottomPadding = bottomPadding;
            _dividers = dividers;
        }

        public float RowHeight { get; }
        public float Spacing { get; }
        public float TopPadding { get; }
        public float BottomPadding { get; }
        public float Stride => RowHeight + Spacing;

        public int DividerCount => _dividers != null ? _dividers.Count : 0;

        public ListDivider DividerAt(int index)
        {
            return _dividers[index];
        }

        public float SlotToTop(float slot)
        {
            float top = TopPadding + slot * Stride;
            if (_dividers == null) return top;

            for (int index = 0; index < _dividers.Count; index++)
            {
                ListDivider divider = _dividers[index];
                float progress = slot - (divider.BeforeSlot - 1);
                if (progress <= 0f) break;
                top += divider.Height * Math.Min(1f, progress);
            }
            return top;
        }

        public float SlotToCenter(float slot)
        {
            return SlotToTop(slot) + RowHeight * 0.5f;
        }

        /// <summary>Mép trên của divider thứ <paramref name="index"/> — ngay trên row mà nó đứng trước.</summary>
        public float DividerTop(int index)
        {
            ListDivider divider = _dividers[index];
            return SlotToTop(divider.BeforeSlot) - divider.Height;
        }

        /// <summary>Chiều cao nội dung đủ chứa tới slot lớn nhất; -1 (không có row) cho 0.</summary>
        public float ContentHeight(float maximumSlot)
        {
            int rowCount = (int)Math.Ceiling(maximumSlot) + 1;
            return rowCount > 0 ? SlotToTop(rowCount - 1) + RowHeight + BottomPadding : 0f;
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

        /// <summary>Nghịch đảo của <see cref="SlotToTop"/>: slot có mép trên nằm ở <paramref name="scroll"/>.</summary>
        public float TopVisibleSlot(float scroll)
        {
            float y = scroll - TopPadding;
            if (_dividers == null) return y / Stride;

            float offset = 0f;
            for (int index = 0; index < _dividers.Count; index++)
            {
                ListDivider divider = _dividers[index];
                float regionStart = (divider.BeforeSlot - 1) * Stride + offset;
                if (y < regionStart) break;

                float regionLength = Stride + divider.Height;
                if (y <= regionStart + regionLength) return divider.BeforeSlot - 1 + (y - regionStart) / regionLength;
                offset += divider.Height;
            }
            return (y - offset) / Stride;
        }

        /// <summary>
        /// Cửa sổ ô mà một list ảo hoá đang giữ view (<c>MotionSettings.IntroUsesListBuffer</c>): từ ô CHỨA điểm
        /// <paramref name="bufferAbove"/> trên mép trên khung nhìn tới ô cuối có mép trên không quá mép dưới khung nhìn +
        /// <paramref name="bufferBelow"/>. Trả ô đầu và SỐ ô (0 khi cửa sổ rỗng); ô âm bị kẹp về 0.
        /// </summary>
        public void BufferedSlots(float scroll, float viewportHeight, float bufferAbove, float bufferBelow, out int firstSlot,
                                  out int slotCount)
        {
            firstSlot = Math.Max(0, (int)Math.Floor(TopVisibleSlot(scroll - Math.Max(0f, bufferAbove))));
            int lastSlot = (int)Math.Floor(TopVisibleSlot(scroll + Math.Max(0f, viewportHeight) + Math.Max(0f, bufferBelow)));
            slotCount = Math.Max(0, lastSlot - firstSlot + 1);
        }

        /// <summary>Gợi ý số row vừa viewport để quyết định tải bao nhiêu; bỏ qua divider vì chỉ cần xấp xỉ.</summary>
        public int VisibleRowCapacity(float viewportHeight)
        {
            return Stride <= 0f ? 0 : Math.Max(0, (int)Math.Floor((viewportHeight + Spacing) / Stride));
        }
    }
}
