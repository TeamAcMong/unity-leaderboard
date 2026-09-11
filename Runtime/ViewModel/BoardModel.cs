using System;
using System.Collections.Generic;

namespace DreamTech.Leaderboard.ViewModel
{
    /// <summary>
    /// Toàn bộ trạng thái hiển thị của một lần trình bày board: các <see cref="RowState"/> + đồng hồ riêng.
    /// Đồng hồ chỉ tiến khi được <see cref="Advance"/>, nên test có thể tua từng tick mà không cần Unity.
    /// </summary>
    public sealed class BoardModel
    {
        private readonly List<RowState> _rows;

        public BoardModel(BoardScene scene, MotionSettings settings)
        {
            Scene = scene ?? throw new ArgumentNullException(nameof(scene));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _rows = new List<RowState>(scene.Rows.Count);
            for (int index = 0; index < scene.Rows.Count; index++)
            {
                var row = new RowState(scene.Rows[index], index);
                _rows.Add(row);
                if (row.IsLocalPlayer && LocalRow == null) LocalRow = row;
            }
        }

        public BoardScene Scene { get; }
        public MotionSettings Settings { get; }
        public IReadOnlyList<RowState> Rows => _rows;

        /// <summary>Row của người chơi; null nếu không có.</summary>
        public RowState LocalRow { get; }

        public RankTierRule TierRule => Scene.TierRule;

        /// <summary>Giây đã trôi kể từ khi model được tạo (theo nhịp Advance, tức unscaled time nhân time-scale debug).</summary>
        public double Clock { get; private set; }

        /// <summary>Tăng khi thêm/bớt row, để list biết cần tính lại chiều cao nội dung.</summary>
        public int StructureVersion { get; private set; }

        public float MaximumSlot
        {
            get
            {
                float maximum = -1f;
                for (int index = 0; index < _rows.Count; index++) maximum = Math.Max(maximum, _rows[index].Slot);
                return maximum;
            }
        }

        public int IndexOf(RowState row)
        {
            return _rows.IndexOf(row);
        }

        public void Advance(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            Clock += deltaTime;
            for (int index = 0; index < _rows.Count; index++) _rows[index].Advance(deltaTime, Settings);
        }

        public void FinishAllTweens()
        {
            for (int index = 0; index < _rows.Count; index++) _rows[index].FinishTweens();
        }

        /// <summary>Các row nổi lên lần lượt tính từ ô trên cùng đang nhìn thấy.</summary>
        public void StartIntro(float topVisibleSlot)
        {
            for (int index = 0; index < _rows.Count; index++)
            {
                RowState row = _rows[index];
                float delay = Math.Min(Settings.MaximumIntroDelay, Math.Max(0f, row.Slot - topVisibleSlot) * Settings.IntroStagger);
                row.StartIntro(delay);
            }
        }

        /// <summary>Tách các row từ startIndex tới cuối (dùng khi quay số để hạng hiển thị không mâu thuẫn).</summary>
        public IReadOnlyList<RowState> DetachRowsFrom(int startIndex)
        {
            if (startIndex < 0 || startIndex >= _rows.Count) return Array.Empty<RowState>();
            List<RowState> tail = _rows.GetRange(startIndex, _rows.Count - startIndex);
            _rows.RemoveRange(startIndex, _rows.Count - startIndex);
            StructureVersion++;
            return tail;
        }

        /// <summary>Gắn lại các row vào cuối, xếp slot liền sau row cuối hiện tại.</summary>
        public void AppendRows(IReadOnlyList<RowState> rows, bool playIntro)
        {
            if (rows == null || rows.Count == 0) return;
            int firstSlot = _rows.Count;
            for (int index = 0; index < rows.Count; index++)
            {
                RowState row = rows[index];
                row.Slot = firstSlot + index;
                if (playIntro) row.StartIntro(Math.Min(Settings.MaximumIntroDelay, index * Settings.IntroStagger));
                _rows.Add(row);
            }
            StructureVersion++;
        }
    }
}
