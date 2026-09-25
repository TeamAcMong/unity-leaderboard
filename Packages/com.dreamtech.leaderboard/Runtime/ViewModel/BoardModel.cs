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
                if (row.IsLocalPlayer && LocalRow == null)
                {
                    LocalRow = row;
                    _localFinalIndex = index;
                }
            }
            HiddenLeadingSlots = CountHiddenLeadingSlots(scene.Rows, settings.HostPresentedTopRanks);
        }

        /// <summary>
        /// Chỉ số của row mình trong bảng CUỐI (cũng là ô nó đáp); -1 khi không có. Không đổi suốt màn diễn: planner chỉ tách
        /// phần đuôi NẰM SAU row mình, nên thứ tự từ đầu bảng tới row mình không bao giờ bị xê dịch.
        /// </summary>
        private readonly int _localFinalIndex = -1;

        public BoardScene Scene { get; }
        public MotionSettings Settings { get; }
        public IReadOnlyList<RowState> Rows => _rows;

        /// <summary>Row của người chơi; null nếu không có.</summary>
        public RowState LocalRow { get; }

        /// <summary>
        /// Số ô đầu list do HOST trình bày (<see cref="MotionSettings.HostPresentedTopRanks"/>): số row ĐẦU bảng cuối, liên
        /// tiếp, không phải "..." và có <c>Rank &lt; K</c> — dừng ở row đầu tiên không thoả, và KHÔNG BAO GIỜ quá K. 0 khi cờ tắt.
        ///
        /// <para>Tính MỘT lần từ bảng cuối (index = ô lúc đứng yên), không từ Slot đang chạy: ranh giới là chỗ của cái bục, không
        /// trôi theo màn diễn.</para>
        ///
        /// <para><b>Trần K:</b> host có đúng K chỗ (bục K cờ), nên list không bao giờ giấu nhiều row hơn thế. Hạng bằng nhau
        /// (backend xếp hạng liền, ví dụ 1, 2, 2, 3) cho nhiều hơn K row có <c>Rank &lt; K</c>: row thừa ở lại list và được vẽ
        /// như thường thay vì bị giấu mà không ai trình bày.</para>
        ///
        /// <para><b>Ít hơn K</b> khi bảng thiếu (tải ít hơn K hạng đầu) hoặc đứt quãng ("..." ngay sau hạng 1): bục chỉ trình bày
        /// những người đứng đầu đang có, và các ô từ giá trị này tới K-1 là row của LIST (có thể là "...") — chúng rơi vào dải
        /// mà host đã chừa cho bục nếu nó hạ <c>topPadding</c> đúng K hàng. Host nào cần khớp tuyệt đối thì đặt bố cục theo con số
        /// này (đọc qua <c>LeaderboardScrollView.Model</c>) thay vì theo K.</para>
        /// </summary>
        public int HiddenLeadingSlots { get; }

        /// <summary>
        /// Row mình ĐÁP vào phần host trình bày (ô cuối &lt; <see cref="HiddenLeadingSlots"/>). Tính theo ô cuối chứ không
        /// theo Slot đang chạy: camera, thanh dính và phần ăn mừng cần biết điều đó từ TRƯỚC khi row tới nơi.
        /// </summary>
        public bool LocalLandsOnPodium => _localFinalIndex >= 0 && _localFinalIndex < HiddenLeadingSlots;

        /// <summary>
        /// Độ hiện diện của row trên LIST, 0..1: 0 = host đang trình bày nó (nằm sau bục hoặc bị host giành bằng
        /// <see cref="RowState.IsHiddenFromList"/>), 1 = list vẽ nó như thường, lẻ = đang trượt ra khỏi bục.
        ///
        /// <para>Ô <c>Hidden - 1</c> trở lên trên là 0, ô <c>Hidden</c> trở xuống là 1, ở giữa đi tuyến tính theo Slot. Tuyến
        /// tính theo Slot (thay vì cắt cứng ở một ngưỡng) để một row đang trượt từ sau bục ra — người vừa mất hạng 3 rơi xuống
        /// thành thanh hạng 4 — hiện dần lên đúng nhịp nó di chuyển. Host dùng CHÍNH hàm này (lấy 1 − giá trị) cho phía bục,
        /// nên chỗ hai bên giao nhau không bao giờ hở hay trùng.</para>
        /// </summary>
        public float ListPresence(RowState row)
        {
            if (row == null || row.IsHiddenFromList) return 0f;
            if (HiddenLeadingSlots <= 0) return 1f;
            return Easing.Clamp01(row.Slot - (HiddenLeadingSlots - 1));
        }

        /// <summary>
        /// Host đang trình bày row này — toàn phần hay một phần (<see cref="ListPresence"/> &lt; 1). Null = false.
        /// </summary>
        public bool IsPresentedByHost(RowState row)
        {
            return row != null && ListPresence(row) < 1f;
        }

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

        /// <summary>
        /// Giây kể từ <see cref="StartIntro(float, int)"/> tới lúc row CUỐI trong vùng nhìn thấy đậu hẳn (độ trễ lớn nhất
        /// trong vùng + <c>IntroDuration</c>). Host dùng để nối nhịp của nó ngay sau đợt trượt vào mà không chép công thức
        /// độ trễ của package. 0 khi chưa có intro.
        /// </summary>
        public float IntroSettleSeconds { get; private set; }

        /// <summary>Các row nổi lên lần lượt tính từ ô trên cùng đang nhìn thấy.</summary>
        public void StartIntro(float topVisibleSlot)
        {
            StartIntro(topVisibleSlot, int.MaxValue);
        }

        /// <summary>Như trên; <paramref name="visibleRowCount"/> giới hạn vùng tính <see cref="IntroSettleSeconds"/>.</summary>
        public void StartIntro(float topVisibleSlot, int visibleRowCount)
        {
            // Ô đầu do host trình bày (bục) không có thanh trên list: thanh ĐẦU TIÊN list vẽ mới là nhịp 0 của đợt trượt, không
            // phải ô 0 nằm sau bục. HiddenLeadingSlots = 0 khi cờ tắt ⇒ y như cũ.
            if (HiddenLeadingSlots > 0) topVisibleSlot = Math.Max(topVisibleSlot, HiddenLeadingSlots);
            float settle = 0f;
            for (int index = 0; index < _rows.Count; index++)
            {
                RowState row = _rows[index];
                float delay = Math.Min(Settings.MaximumIntroDelay, Math.Max(0f, row.Slot - topVisibleSlot) * Settings.IntroStagger);
                delay += Math.Max(0f, Settings.IntroRowDelayOffset);
                row.StartIntro(delay);
                float offset = row.Slot - topVisibleSlot;
                if (offset > -1f && offset < visibleRowCount) settle = Math.Max(settle, delay + Math.Max(0f, Settings.IntroDuration));
            }
            IntroSettleSeconds = settle;
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

        private static int CountHiddenLeadingSlots(IReadOnlyList<BoardRow> sceneRows, int hostPresentedTopRanks)
        {
            if (hostPresentedTopRanks <= 0) return 0;
            int count = 0;
            // Dừng ở K: host chỉ có K chỗ, row thứ K+1 (hạng bằng nhau) phải ở lại list — xem HiddenLeadingSlots.
            for (int index = 0; index < sceneRows.Count && count < hostPresentedTopRanks; index++)
            {
                BoardRow row = sceneRows[index];
                if (row.IsGap || row.Entry == null || row.Entry.Rank >= hostPresentedTopRanks) break;
                count++;
            }
            return count;
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
