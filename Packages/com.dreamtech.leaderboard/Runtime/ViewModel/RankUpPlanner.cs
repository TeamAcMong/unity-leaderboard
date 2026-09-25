using System;
using System.Collections.Generic;

namespace DreamTech.Leaderboard.ViewModel
{
    /// <summary>Kết quả dựng trạng thái cũ cho một màn lên hạng.</summary>
    public sealed class RankUpPlan
    {
        internal RankUpPlan(int localIndex, int animatedCount, int passedTotal, bool isCompressed, float startSlot,
                            IReadOnlyList<RowState> passed, IReadOnlyList<RowState> tail, int spinFromRank, int spinToRank)
        {
            LocalIndex = localIndex;
            AnimatedCount = animatedCount;
            PassedTotal = passedTotal;
            IsCompressed = isCompressed;
            StartSlot = startSlot;
            Passed = passed;
            Tail = tail;
            SpinFromRank = spinFromRank;
            SpinToRank = spinToRank;
        }

        /// <summary>Chỉ số row người chơi trong dữ liệu MỚI (cũng là slot đích).</summary>
        public int LocalIndex { get; }

        /// <summary>
        /// Số người được diễn vượt từng người. Tối đa <c>AnimatedPasses</c> của cửa sổ tải — trừ khi row mình đáp vào phần host
        /// trình bày (<c>MotionSettings.HostPresentedTopRanks</c>): khi đó có thể nhiều hơn, đủ để phủ trọn phần đó.
        /// </summary>
        public int AnimatedCount { get; }

        /// <summary>Tổng số người bị vượt (hiển thị trên pill "▲N").</summary>
        public int PassedTotal { get; }

        /// <summary>Nhảy nhiều hơn số diễn được: quay số trước khi leo.</summary>
        public bool IsCompressed { get; }

        /// <summary>Slot của row người chơi ở trạng thái cũ.</summary>
        public float StartSlot { get; }

        /// <summary>Các row bị vượt; phần tử 0 là người ngay trên mình (bị vượt đầu tiên).</summary>
        public IReadOnlyList<RowState> Passed { get; }

        /// <summary>Các row phía dưới bị tách tạm khi quay số, gắn lại sau khi hạ cánh.</summary>
        public IReadOnlyList<RowState> Tail { get; }

        public int SpinFromRank { get; }
        public int SpinToRank { get; }

        public float FinalSlot => LocalIndex;
    }

    /// <summary>
    /// Dựng "trạng thái cũ" từ dữ liệu MỚI cho màn lên hạng. Giữ nguyên công thức đã kiểm chứng của bản tham khảo:
    /// người bị vượt Slot = index - 1 và hạng = Rank - 1; row mình Slot = index + count; luật vượt
    /// <c>slot &lt;= startSlot - crossed - makeRoomAt</c>.
    /// </summary>
    public static class RankUpPlanner
    {
        public static RankUpPlan Prepare(BoardModel model, in RankChange change)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (change.Kind != RankChangeKind.RankUp) throw new ArgumentException("Chỉ dựng kế hoạch cho RankUp.", nameof(change));
            RowState local = model.LocalRow ?? throw new InvalidOperationException("Board không có row người chơi.");

            int localIndex = model.IndexOf(local);
            int maximumAnimated = model.Scene.Plan.AnimatedPasses;
            IReadOnlyList<RowState> rows = model.Rows;

            // Chỉ diễn những row liền mạch ngay dưới mình (phòng backend trả thiếu dữ liệu).
            int count = 0;
            for (int offset = 1; offset <= maximumAnimated && localIndex + offset < rows.Count; offset++)
            {
                RowState row = rows[localIndex + offset];
                if (row.IsGap || row.Entry.Rank != change.ToRank + offset) break;
                count = offset;
            }

            int passedTotal = change.PassedCount;
            count = ExtendToCoverHostPresentedRows(model, localIndex, count, passedTotal);
            bool compressed = passedTotal > count;

            IReadOnlyList<RowState> tail = Array.Empty<RowState>();
            int tailStart = localIndex + count + 1;
            if (compressed && tailStart < rows.Count) tail = model.DetachRowsFrom(tailStart);

            // Duyệt từ dưới lên: passed[0] là row có slot cũ localIndex + count - 1, tức ngay trên mình, bị vượt đầu tiên.
            var passed = new List<RowState>(count);
            for (int offset = count; offset >= 1; offset--)
            {
                RowState row = model.Rows[localIndex + offset];
                row.Slot = localIndex + offset - 1;
                row.SetDisplayRankImmediate(row.Entry.Rank - 1);
                passed.Add(row);
            }

            float startSlot = localIndex + count;
            local.Slot = startSlot;
            local.SetDisplayRankImmediate(change.FromRank);
            local.SetDisplayScore(change.FromScore, model.Clock, false);

            return new RankUpPlan(localIndex, count, passedTotal, compressed, startSlot, passed, tail,
                                  change.FromRank, change.ToRank + count);
        }

        /// <summary>
        /// Row mình đáp vào phần host trình bày (<see cref="BoardModel.HiddenLeadingSlots"/> &gt; 0, ô đích &lt; ranh giới): nới
        /// số người diễn vượt để trạng thái cũ dựng lại được TRỌN phần đó — mọi người từ ô đích tới ô ranh giới, kể cả người cũ
        /// hạng K (người sẽ rơi xuống thành thanh đầu list), nhưng không quá số người thật sự bị vượt.
        ///
        /// <para>Vì sao: <c>AnimatedPasses</c> nhỏ hơn khoảng cách tới ranh giới (ví dụ 2 lượt, #41 → #1 với bục 3 cờ), hoặc dải
        /// hạng liền mạch bị cắt ngắn vì hạng bằng nhau, thì row mình xuất phát ở ô &lt; ranh giới — SAU bục, list không bao giờ
        /// vẽ nó — trong khi người cũ hạng K lại nằm trong phần đuôi bị tách. Bục "trước" mà host dựng từ các ô &lt; ranh giới khi
        /// đó có người chơi mang số hạng 41 thay cho người cũ hạng 3. Nới tới ranh giới thì row mình xuất phát ở ô ranh giới (thanh
        /// hạng K+1, list vẽ), đúng như một màn lên bục từ list.</para>
        ///
        /// <para>Chỉ nới qua các row thật đang có (dừng ở "..." hoặc cuối bảng — không có dữ liệu thì không dựng được). Cờ tắt
        /// (<c>HiddenLeadingSlots</c> = 0) hoặc không đáp vào phần đó thì trả nguyên <paramref name="count"/>: kế hoạch y như cũ.</para>
        /// </summary>
        private static int ExtendToCoverHostPresentedRows(BoardModel model, int localIndex, int count, int passedTotal)
        {
            int hiddenLeadingSlots = model.HiddenLeadingSlots;
            if (hiddenLeadingSlots <= 0 || localIndex >= hiddenLeadingSlots) return count;

            IReadOnlyList<RowState> rows = model.Rows;
            int requiredCount = Math.Min(passedTotal, hiddenLeadingSlots - localIndex);
            for (int offset = count + 1; offset <= requiredCount && localIndex + offset < rows.Count; offset++)
            {
                if (rows[localIndex + offset].IsGap) break;
                count = offset;
            }
            return count;
        }

        public static float ClimbDuration(int count, MotionSettings settings)
        {
            return Easing.Clamp(count * settings.ClimbSecondsPerRow, settings.ClimbDurationMinimum, settings.ClimbDurationMaximum);
        }

        /// <summary>Người phía trên nhường chỗ TRƯỚC khi row mình tới, nên độ chồng lấn tối đa bằng makeRoomAt.</summary>
        public static bool ShouldCross(float localSlot, float startSlot, int crossedCount, float makeRoomAt)
        {
            return localSlot <= startSlot - crossedCount - makeRoomAt;
        }
    }
}
