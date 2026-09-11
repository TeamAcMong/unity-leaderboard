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

        /// <summary>Số người được diễn vượt từng người.</summary>
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
