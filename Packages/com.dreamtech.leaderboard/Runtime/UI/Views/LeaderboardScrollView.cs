using System.Collections.Generic;
using DreamTech.Leaderboard.ViewModel;
using UnityEngine;
using UnityEngine.UI;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>
    /// List ảo hoá theo Slot: chỉ giữ đủ view cho số dòng nhìn thấy, tái sử dụng khi cuộn. Không dùng LayoutGroup.
    ///
    /// <list type="bullet">
    /// <item>Chạy sau ScrollRect (DefaultExecutionOrder 100) để đọc vị trí cuộn cuối frame.</item>
    /// <item>Trong lúc diễn, view của row người chơi được GHIM (không bao giờ bị thu hồi), và yêu cầu snap camera được xử lý
    /// ngay trong frame, trước khi kiểm vùng nhìn thấy — hai lỗi gốc làm pill/shine biến mất sau skip ở bản cũ.</item>
    /// <item>Vị trí effect (sao, confetti, banner) tính từ slot qua <see cref="TryGetRowBounds"/>, không lấy từ view trong pool.</item>
    /// <item>Cờ <c>MotionSettings.HostPresentedTopRanks</c> bật: các ô đầu do host trình bày (bục) không có view; row trượt ra
    /// khỏi bục hiện dần theo <see cref="ListPresence"/>; camera coi đỉnh list là nhà khi row mình đáp lên bục; không có thanh
    /// dính cho người chơi đang ở trên bục. Host mượn hình thanh bằng <see cref="CreateRowProxy"/>.</item>
    /// <item>Cú tiếp cận bục kiểu cuộn (<c>MotionSettings.PodiumApproachScrollSpeed</c>, <c>BoardModel.HasPodiumApproach</c>):
    /// mở màn canh giữa ô xuất phát, camera đi theo đúng tiến độ của row mình, và — nếu prefab có
    /// <see cref="FloatingRowLayer"/> — view của row mình được vẽ ở lớp nổi đó, ngoài mask của list.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class LeaderboardScrollView : MonoBehaviour
    {
        [Header("Tham chiếu")]
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform viewport;
        [SerializeField] private RectTransform content;
        [Tooltip("Prefab row (asset). Prefab variant của widget có thể trỏ sang row variant của game.")]
        [SerializeField] private LeaderboardEntryView rowPrefab;
        [Tooltip("Tia sáng phía sau row người chơi (con của Content), chỉ dùng cho top 3 / #1.")]
        [SerializeField] private RankSunburstView sunburst;

        [Header("Thanh 'hạng của bạn' dính mép")]
        [SerializeField] private RectTransform stickyAnchor;
        [SerializeField] private CanvasGroup stickyGroup;
        [SerializeField] private Button stickyButton;

        [Header("Bố cục")]
        [SerializeField, Min(1f)] private float rowHeight = 124f;
        [SerializeField, Min(0f)] private float spacing = 12f;
        [Tooltip("Phải ≥ phần row tràn lên trên khi nhấc (pill, glow, scale), nếu không row #1 bị mask cắt.")]
        [SerializeField, Min(0f)] private float topPadding = 44f;
        [Tooltip("Phải ≥ phần bóng đổ tràn xuống dưới.")]
        [SerializeField, Min(0f)] private float bottomPadding = 56f;
        [SerializeField, Min(0f)] private float stickyInset = 12f;
        [Tooltip("Số view tạo sẵn khi dựng lần đầu.")]
        [SerializeField, Min(0)] private int prewarmRowCount = 12;

        [Header("Lớp nổi của row mình (0.6.0, tuỳ chọn)")]
        [Tooltip("RectTransform RIÊNG nằm NGOÀI mask của list (không phải con của viewport), không chứa gì khác. Trong cú tiếp " +
                 "cận bục kiểu cuộn (MotionSettings.PodiumApproachScrollSpeed > 0) list đặt nó trùng khung Content mỗi frame và vẽ " +
                 "view của row mình trong đó, để ô ranh giới nằm dưới đáy khung nhìn (màn thấp, 4:3) vẫn thấy row. Trống = như cũ.")]
        [SerializeField] private RectTransform floatingRowLayer;

        [Header("Ngưỡng thanh dính")]
        [SerializeField] private float stickyShowFraction = 0.6f;
        [SerializeField] private float stickyHideFraction = 0.4f;
        [SerializeField] private float stickyFadeSpeed = 5f;
        [SerializeField] private float stickySlideFraction = 0.8f;

        private readonly Dictionary<RowState, LeaderboardEntryView> _activeViews = new Dictionary<RowState, LeaderboardEntryView>();
        private readonly Stack<LeaderboardEntryView> _pool = new Stack<LeaderboardEntryView>();
        private readonly List<RowState> _releaseBuffer = new List<RowState>();
        private readonly HashSet<RowState> _rowsInModel = new HashSet<RowState>();

        private bool _isInitialized;
        private BoardModel _model;
        private LeaderboardRenderContext _context;
        private int _knownStructureVersion = -1;
        private float _followVelocity;

        /// <summary>
        /// Mốc của camera NEO (bám cứng, <c>FollowSmoothTime ≤ 0</c>): slot và vị trí cuộn lúc bắt đầu bám.
        /// <c>float.NaN</c> = chưa chụp — chụp ở khung đầu tiên bám sau mỗi <see cref="SetModel"/>.
        /// </summary>
        private float _anchorStartSlot = float.NaN;
        private float _anchorStartScroll;
        private bool _isSnapRequested;
        private bool _isSmoothScrolling;
        private float _smoothScrollFrom;
        private float _smoothScrollTo;
        private float _smoothScrollElapsed;
        private float _smoothScrollDuration;

        private LeaderboardEntryView _stickyView;
        private float _stickyVisibility;
        private int _stickySide;

        private readonly List<LeaderboardRankDivider> _rankDividers = new List<LeaderboardRankDivider>();
        private readonly List<ListDivider> _resolvedDividers = new List<ListDivider>();
        private readonly List<RectTransform> _resolvedDividerViews = new List<RectTransform>();

        public bool FollowLocalRow { get; set; }

        /// <summary>Không bao giờ thu hồi view của row người chơi (bật trong lúc diễn).</summary>
        public bool PinLocalRow { get; set; }

        public bool ForceHideSticky { get; set; }

        /// <summary>
        /// Mặc định TẮT (= hành vi cũ: mở màn canh vào row người chơi). Bật thì mỗi lần <see cref="SetModel"/> KHÔNG có cú tiếp cận
        /// bục mở list ở ĐỈNH (chỗ cuộn 0) — cho màn chỉ-xem-bảng mà host tự hiện row người chơi ở chỗ khác (thanh ghim của host) và
        /// muốn người chơi thấy đầu bảng trước. Đợt trượt vào tính theo chỗ cuộn này nên các hàng ở đỉnh trượt vào đúng nhịp.
        /// Host đặt trước khi trình bày; list không tự đổi cờ này.
        /// </summary>
        public bool OpenAtTop { get; set; }

        /// <summary>Hệ số thời gian debug (chụp màn hình chậm). 1 = bình thường.</summary>
        public float TimeScale { get; set; } = 1f;

        public BoardModel Model => _model;
        public RectTransform Viewport => viewport;
        public RectTransform Content => content;

        /// <summary>Lớp nổi của row mình (xem field <c>floatingRowLayer</c>); null = không bao giờ vẽ row ngoài mask.</summary>
        public RectTransform FloatingRowLayer => floatingRowLayer;
        public LeaderboardEntryView RowPrefab => rowPrefab;
        public float RowHeight => rowHeight;
        public float TopPadding => topPadding;
        public float BottomPadding => bottomPadding;

        public VirtualListLayout Layout => _resolvedDividers.Count == 0
            ? new VirtualListLayout(rowHeight, spacing, topPadding, bottomPadding)
            : new VirtualListLayout(rowHeight, spacing, topPadding, bottomPadding, _resolvedDividers);

        /// <summary>
        /// Chèn dải giữa các hạng (Promotion / Demotion của League, tiêu đề nhóm...). View do game cấp, là con của
        /// <see cref="Content"/>; list chỉ đặt vị trí và bật/tắt. Dải chỉ hiện khi đúng hạng ranh giới đang có trong bảng —
        /// cửa sổ tải không chạm tới ranh giới thì dải ẩn chứ không đặt bừa.
        ///
        /// <para>Gọi lúc nào cũng được (trước hoặc sau khi có model); mỗi lần bảng đổi cấu trúc list tự tính lại.</para>
        /// </summary>
        public void SetRankDividers(IReadOnlyList<LeaderboardRankDivider> dividers)
        {
            _rankDividers.Clear();
            if (dividers != null)
            {
                for (int index = 0; index < dividers.Count; index++) _rankDividers.Add(dividers[index]);
            }
            RefreshContentHeight();
        }

        public int VisibleRowCapacity => Layout.VisibleRowCapacity(ViewportHeight);

        private float ViewportHeight => viewport ? viewport.rect.height : 0f;
        private float ContentHeight => content ? content.sizeDelta.y : 0f;
        private float MaximumScroll => Layout.MaximumScroll(ContentHeight, ViewportHeight);

        public void Configure(LeaderboardRenderContext context)
        {
            _context = context;
        }

        /// <summary>Dựng list cho một model mới: căn giữa row người chơi (nếu có) và cho các row nổi lên lần lượt.</summary>
        public void SetModel(BoardModel model, bool playIntro)
        {
            BindModel(model, playIntro, isStaged: false);
        }

        /// <summary>
        /// Dựng list cho một model TRƯỚC khi host sẵn sàng (<c>LeaderboardVisualSettings.StageListBeforeHostReady</c>): mép trên ô của
        /// row người chơi sát mép trên khung nhìn (kẹp trong khoảng cuộn được) — đỉnh list khi row đó đang nằm trong phần host trình
        /// bày, không có row người chơi, hoặc list mở ở đỉnh (<see cref="OpenAtTop"/>) — và mọi row ở tư thế đầu của đợt trượt vào
        /// (chưa hiện). Đợt trượt không chạy tới khi widget tick model; lượt trình bày thật gọi <see cref="SetModel"/> với CÙNG model
        /// để canh lại và bắt đầu đợt trượt.
        /// </summary>
        public void StageModel(BoardModel model)
        {
            BindModel(model, playIntro: true, isStaged: true);
        }

        private void BindModel(BoardModel model, bool playIntro, bool isStaged)
        {
            EnsureInitialized();
            ReleaseAllViews();
            StopSmoothScroll();
            _model = model;
            _knownStructureVersion = -1;
            _followVelocity = 0f;
            _anchorStartSlot = float.NaN;
            _isSnapRequested = false;
            if (sunburst) sunburst.Stop();
            if (_model == null)
            {
                RefreshContentHeight();
                return;
            }

            RebuildRowSet();
            RefreshContentHeight();
            if (scrollRect) scrollRect.StopMovement();
            RowState localRow = _model.LocalRow;
            bool opensAtTop = OpenAtTop && !_model.HasPodiumApproach;
            if (isStaged)
            {
                SetScrollY(localRow != null && !opensAtTop ? StagedScrollTarget(localRow) : 0f);
            }
            else
            {
                SetScrollY(localRow != null && !opensAtTop ? IntroScrollTarget(localRow) : 0f);
                // Cú tiếp cận cuộn từ ĐÚNG chỗ này về đỉnh list (hay dừng hụt): báo hai đầu (đã kẹp) cho timeline tính thời lượng.
                if (_model.HasPodiumApproach) ReportPodiumApproachScrollRange();
            }
            if (playIntro) StartModelIntro();
            Refresh(0f);
        }

        /// <summary>
        /// Báo hai đầu của cú tiếp cận bục kiểu cuộn cho model: chỗ cuộn lúc mở màn, và chỗ cuộn cuối — 0 (đỉnh list), hay
        /// <c>MotionSettings.PodiumApproachShortfallRows</c> × bước hàng khi lúc này vùng host trình bày ĐÃ khuất hẳn (chỗ cuộn ≥ mép
        /// trên ô ranh giới <c>BoardModel.HiddenLeadingSlots</c>).
        /// </summary>
        private void ReportPodiumApproachScrollRange()
        {
            float start = GetScrollY();
            MotionSettings settings = _model.Settings;
            float shortfallRows = settings != null ? settings.PodiumApproachShortfallRows : 0f;
            bool hostRegionOutOfView = start >= Layout.SlotToTop(_model.HiddenLeadingSlots);
            float end = shortfallRows > 0f && hostRegionOutOfView ? shortfallRows * Layout.Stride : 0f;
            _model.SetPodiumApproachScrollRange(start, end);
        }

        /// <summary>
        /// Đợt trượt vào: mặc định các ô vừa khung nhìn tính từ ô trên cùng đang thấy; <c>MotionSettings.IntroUsesListBuffer</c>
        /// thì cửa sổ ô mà một list ảo hoá đang giữ view (<see cref="VirtualListLayout.BufferedSlots"/>), đệm dưới lớn hơn khi
        /// list mở ở một chỗ cuộn khác 0. Ô host trình bày bị <see cref="BoardModel.StartIntro(float, int)"/> cắt ra.
        /// </summary>
        private void StartModelIntro()
        {
            float scroll = GetScrollY();
            MotionSettings settings = _model.Settings;
            if (settings == null || !settings.IntroUsesListBuffer)
            {
                _model.StartIntro(Layout.TopVisibleSlot(scroll), VisibleRowCapacity + 1);
                return;
            }
            float bufferBelow = scroll > 0f ? settings.IntroBufferBelowRecentred : settings.IntroBufferBelow;
            Layout.BufferedSlots(scroll, ViewportHeight, settings.IntroBufferAbove, bufferBelow, out int firstSlot, out int slotCount);
            int visibleFirst = Mathf.Max(firstSlot, _model.HiddenLeadingSlots);
            _model.StartIntro(visibleFirst, Mathf.Max(0, firstSlot + slotCount - visibleFirst));
        }

        /// <summary>
        /// Chỗ cuộn của list dựng sẵn (<see cref="StageModel"/>): mép trên ô của row mình sát mép trên khung nhìn, kẹp trong khoảng
        /// cuộn được; đỉnh list khi row đó đang nằm trong phần host trình bày (ô &lt; <see cref="BoardModel.HiddenLeadingSlots"/>).
        /// </summary>
        private float StagedScrollTarget(RowState localRow)
        {
            if (localRow.Slot < _model.HiddenLeadingSlots) return 0f;
            return Mathf.Clamp(Layout.SlotToTop(localRow.Slot), 0f, MaximumScroll);
        }

        /// <summary>
        /// Chỗ cuộn lúc mở màn: như <see cref="LocalRowScrollTarget"/>, trừ cú tiếp cận bục kiểu cuộn — khi đó LUÔN canh giữa
        /// ô xuất phát, kể cả ô ranh giới (camera phải đi một quãng cuộn thật về đỉnh list cùng nhịp với row mình).
        /// </summary>
        private float IntroScrollTarget(RowState localRow)
        {
            return _model.HasPodiumApproach
                ? Layout.CenteredScroll(localRow.Slot, ViewportHeight, MaximumScroll)
                : LocalRowScrollTarget(localRow);
        }

        public void Clear()
        {
            SetModel(null, false);
            FollowLocalRow = false;
            PinLocalRow = false;
            HideStickyImmediately();
        }

        /// <summary>Camera nhảy tới row người chơi ngay trong lần refresh kế tiếp (cùng frame).</summary>
        public void RequestSnapToLocalRow()
        {
            _isSnapRequested = true;
        }

        public void SetUserScroll(bool isEnabled)
        {
            if (!scrollRect) return;
            scrollRect.StopMovement();
            scrollRect.enabled = isEnabled;
        }

        public void ScrollToLocalRow()
        {
            if (_model == null || _model.LocalRow == null || _context == null) return;
            if (scrollRect) scrollRect.StopMovement();
            _isSmoothScrolling = true;
            _smoothScrollFrom = GetScrollY();
            _smoothScrollTo = LocalRowScrollTarget(_model.LocalRow);
            _smoothScrollElapsed = 0f;
            _smoothScrollDuration = Mathf.Max(0.0001f, _context.Motion.ScrollToLocalDuration);
        }

        public void PlaySunburst(Color color, float scale, LeaderboardVisualSettings visuals)
        {
            if (!sunburst || _model == null || _model.LocalRow == null) return;
            // Row mình đang do host trình bày (sau bục) thì không có thanh nào để tia sáng toả ra sau — tia sáng giữa một khoảng
            // trống, đè lên các row khác (nó còn bị đẩy lên sibling cuối), là thứ tệ hơn không có gì.
            if (_model.ListPresence(_model.LocalRow) <= 0f) return;
            sunburst.TimeScale = TimeScale;
            sunburst.Play(color, scale, visuals);
            PositionSunburst();
            OrderLocalRowOnTop();
        }

        /// <summary>Hình chữ nhật của một row (tính từ slot, không từ view) trong hệ toạ độ local của <paramref name="space"/>.</summary>
        public bool TryGetRowBounds(RowState row, RectTransform space, out Rect bounds)
        {
            bounds = default;
            if (row == null || content == null || space == null) return false;

            float halfWidth = (content.rect.width + (rowPrefab ? rowPrefab.RectTransform.sizeDelta.x : 0f)) * 0.5f * row.Scale;
            float halfHeight = rowHeight * 0.5f * row.Scale;
            float centerY = content.rect.yMax - Layout.SlotToCenter(row.Slot) - row.IntroOffset;
            // Cộng cả độ lùi ngang: trong lúc intro row chưa về chỗ, và người gọi TryGetRowBounds (ví dụ
            // đường bay của phần thưởng) phải nhắm vào chỗ row ĐANG đứng chứ không phải chỗ nó sẽ đứng.
            float centerX = content.rect.center.x + row.IntroOffsetX;

            Vector3 worldMinimum = content.TransformPoint(new Vector3(centerX - halfWidth, centerY - halfHeight, 0f));
            Vector3 worldMaximum = content.TransformPoint(new Vector3(centerX + halfWidth, centerY + halfHeight, 0f));
            Vector3 localMinimum = space.InverseTransformPoint(worldMinimum);
            Vector3 localMaximum = space.InverseTransformPoint(worldMaximum);
            bounds = Rect.MinMaxRect(Mathf.Min(localMinimum.x, localMaximum.x), Mathf.Min(localMinimum.y, localMaximum.y),
                                     Mathf.Max(localMinimum.x, localMaximum.x), Mathf.Max(localMinimum.y, localMaximum.y));
            return true;
        }

        // ---------------------------------------------------------------- Phần host trình bày (MotionSettings.HostPresentedTopRanks)

        /// <summary>
        /// Độ hiện diện của row trên list (0..1) — đúng con số list dùng để quyết định có view hay không và nhân vào độ đục.
        /// Host vẽ phần của mình theo <c>1 − ListPresence</c> để hai bên giao nhau khớp từng frame. Chưa có model = 0.
        /// Xem <see cref="BoardModel.ListPresence"/>.
        /// </summary>
        public float ListPresence(RowState row)
        {
            return _model != null ? _model.ListPresence(row) : 0f;
        }

        /// <summary>
        /// Vị trí (anchoredPosition, hệ toạ độ của <see cref="Content"/>) mà list đặt một row đứng yên ở
        /// <paramref name="slot"/> — cùng công thức với Refresh, kể cả divider. Dùng làm điểm đầu / điểm cuối cho proxy của
        /// host: một proxy là con của Content (hoặc của một lớp phủ trùng khung Content) đặt ở đây sẽ trùng khít thanh thật.
        /// </summary>
        public Vector2 GetRowAnchoredPosition(float slot)
        {
            return new Vector2(0f, -Layout.SlotToCenter(slot));
        }

        /// <summary>
        /// Ngữ cảnh render hiện tại (theme, chữ, đồng hồ model). Host cần nó khi muốn <c>Render</c> lại một proxy mỗi frame
        /// cho proxy "sống" theo <see cref="RowState"/> (điểm đang đếm, flash...). Null trước lần trình bày đầu tiên.
        /// </summary>
        public LeaderboardRenderContext RenderContext => _context;

        /// <summary>
        /// Tạo một BẢN SAO của thanh row để host bay / biến hình (thanh thành cờ, cờ rơi xuống thành thanh): instantiate prefab
        /// row dưới <paramref name="parent"/>, bind với ngữ cảnh hiện tại, và vẽ đúng DÁNG list đang vẽ row đó: vị trí
        /// (<see cref="GetRowAnchoredPosition"/> của <c>row.Slot</c> cộng độ lùi intro nếu còn — đúng chỗ khi parent trùng khung
        /// Content), cỡ <c>row.Scale × row.IntroScale</c>, bóng đổ theo <c>row.Lift</c>. Chỉ độ đục khác: luôn đầy (bỏ qua độ
        /// hiện diện — proxy tồn tại chính là để vẽ row mà list đã / sắp nhường cho host; host tự điều độ đục).
        ///
        /// <para>Cỡ lấy từ row chứ không đặt 1: lúc host nhận quyền (<c>PodiumTakeover</c>) row mình còn đang ở dáng nhấc
        /// (<c>row.Scale</c> = <c>LiftScale</c>, ví dụ 1,06). Proxy cỡ 1 thay cho thanh 1,06 là thanh co lại ~6 % trong đúng một
        /// frame, ngay khung đầu của cú bay. Proxy KHÔNG chạy intro: đây là bản chụp dáng hiện tại, host diễn tiếp từ đó.</para>
        ///
        /// <para>Proxy KHÔNG thuộc pool và list không bao giờ đụng vào nó: host sở hữu nó và phải trả bằng
        /// <see cref="DestroyRowProxy"/>. Tách khỏi pool là có chủ ý — view trong pool bị thu hồi / bind lại bất cứ frame nào
        /// row trượt ra ngoài vùng nhìn thấy, không thể cho host mượn giữa chừng.</para>
        ///
        /// <para>Dòng mũi tên lên hạng (nếu prefab có) bị tắt: nó là hiệu ứng hạt chạy theo từng frame, trên một bản chụp
        /// tĩnh các mũi tên sẽ đứng khựng giữa không trung. Host muốn proxy sống thì tự gọi <c>Render</c> với
        /// <see cref="RenderContext"/>.</para>
        ///
        /// <para>Trả null khi chưa có prefab row / chưa có ngữ cảnh (list chưa được Configure) / tham số null — host bỏ qua phần
        /// proxy nhưng vẫn phải thả cổng bục.</para>
        /// </summary>
        public LeaderboardEntryView CreateRowProxy(RowState row, RectTransform parent)
        {
            if (row == null || parent == null || rowPrefab == null || _context == null) return null;

            LeaderboardEntryView proxy = Instantiate(rowPrefab, parent, false);
            proxy.name = rowPrefab.name + " (Proxy)";
            if (!proxy.gameObject.activeSelf) proxy.gameObject.SetActive(true);
            proxy.Bind(row, _context);
            // Cùng công thức vị trí / cỡ với Refresh để lúc bàn giao không lệch một pixel nào.
            Vector2 position = GetRowAnchoredPosition(row.Slot) + new Vector2(row.IntroOffsetX, -row.IntroOffset);
            proxy.Render(row, position, row.Scale * row.IntroScale, 1f, row.Lift, _context);
            LeaderboardRowRankUpStream rankUpStream = proxy.GetComponentInChildren<LeaderboardRowRankUpStream>(true);
            if (rankUpStream) rankUpStream.Hide();
            return proxy;
        }

        /// <summary>
        /// Huỷ một proxy tạo bởi <see cref="CreateRowProxy"/>. Null hoặc một view của chính list (trong pool, đang hiện, thanh
        /// dính) thì bỏ qua — huỷ nhầm view của pool là list mất một view mà vẫn giữ tham chiếu tới nó.
        /// </summary>
        public void DestroyRowProxy(LeaderboardEntryView proxy)
        {
            if (proxy == null) return;
            if (ReferenceEquals(proxy, _stickyView) || _pool.Contains(proxy) || _activeViews.ContainsValue(proxy)) return;

            proxy.Unbind();
            if (Application.isPlaying) Destroy(proxy.gameObject);
            else DestroyImmediate(proxy.gameObject);
        }

        internal LeaderboardEntryView GetActiveView(RowState row)
        {
            return row != null && _activeViews.TryGetValue(row, out LeaderboardEntryView view) ? view : null;
        }

        /// <summary>
        /// View đang vẽ <paramref name="row"/> ở frame này (false = row ngoài vùng nhìn thấy, chưa có view). Cho host gắn một
        /// hiệu ứng NGẮN lên đúng mảnh của row (ví dụ icon điểm nảy lên khi một vật bay tới đáp).
        ///
        /// <para>View là của pool: hỏi lại mỗi lần cần, KHÔNG giữ tham chiếu qua frame — row cuộn ra khỏi màn thì view đó được
        /// thu hồi và gắn cho row khác.</para>
        /// </summary>
        public bool TryGetRowView(RowState row, out LeaderboardEntryView view)
        {
            view = GetActiveView(row);
            return view != null;
        }

        internal int ActiveViewCount => _activeViews.Count;

        /// <summary>Cho test EditMode (không có LateUpdate).</summary>
        internal void RefreshForTests(float deltaTime)
        {
            Refresh(deltaTime);
        }

        private void LateUpdate()
        {
            if (_model == null || _context == null) return;
            Refresh(Time.unscaledDeltaTime * TimeScale);
        }

        // ---------------------------------------------------------------- Refresh

        private void Refresh(float deltaTime)
        {
            if (_model == null || _context == null || content == null || viewport == null) return;
            EnsureInitialized();

            if (_model.StructureVersion != _knownStructureVersion) RebuildRowSet();
            _context.Clock = _model.Clock;
            _context.DeltaTime = deltaTime;

            UpdateCamera(deltaTime);

            VirtualListLayout layout = Layout;
            float viewTop = GetScrollY();
            float viewBottom = viewTop + ViewportHeight;
            float margin = rowHeight * 0.5f;
            RowState localRow = _model.LocalRow;

            _releaseBuffer.Clear();
            foreach (KeyValuePair<RowState, LeaderboardEntryView> pair in _activeViews)
            {
                RowState row = pair.Key;
                bool isPinned = PinLocalRow && row == localRow;
                bool isStillInModel = _rowsInModel.Contains(row);
                // Host đang trình bày row này (độ hiện diện 0) thì thu hồi view, KỂ CẢ khi đang ghim: ghim là để row mình không
                // biến mất khi trượt ra ngoài vùng nhìn thấy, không phải để giữ một thanh nằm đè lên cờ của bục.
                bool isPresentedByHost = _model.ListPresence(row) <= 0f;
                if (!isStillInModel || isPresentedByHost ||
                    (!isPinned && !layout.Intersects(row.Slot, viewTop - margin, viewBottom + margin)))
                {
                    _releaseBuffer.Add(row);
                }
            }
            for (int index = 0; index < _releaseBuffer.Count; index++)
            {
                RowState row = _releaseBuffer[index];
                ReleaseView(_activeViews[row]);
                _activeViews.Remove(row);
            }

            bool hasActivatedView = false;
            IReadOnlyList<RowState> rows = _model.Rows;
            for (int index = 0; index < rows.Count; index++)
            {
                RowState row = rows[index];
                if (_activeViews.ContainsKey(row)) continue;
                if (_model.ListPresence(row) <= 0f) continue;
                bool isPinned = PinLocalRow && row == localRow;
                if (!isPinned && !layout.Intersects(row.Slot, viewTop - margin, viewBottom + margin)) continue;
                LeaderboardEntryView view = AcquireView();
                view.Bind(row, _context);
                _activeViews[row] = view;
                hasActivatedView = true;
            }

            // Chỉ canh lớp nổi khi row mình thật sự có view để đặt vào đó — row đã rời list (host giành thanh, đã lên bục) thì
            // lớp nổi rỗng, không cần đi theo Content nữa.
            bool isLocalRowFloating = IsLocalRowFloating && localRow != null && _activeViews.ContainsKey(localRow);
            if (isLocalRowFloating) MirrorContentOnto(floatingRowLayer);

            foreach (KeyValuePair<RowState, LeaderboardEntryView> pair in _activeViews)
            {
                RowState row = pair.Key;
                LeaderboardEntryView view = pair.Value;
                if (!ReferenceEquals(view.BoundRow, row) || view.BoundContentVersion != row.ContentVersion) view.Bind(row, _context);
                // Lớp nổi trùng khung Content nên vị trí / cỡ tính trong hệ Content vẫn đúng y nguyên sau khi đổi cha.
                PlaceUnderParent(view, isLocalRowFloating && row == localRow ? floatingRowLayer : content);
                var position = new Vector2(row.IntroOffsetX, -layout.SlotToCenter(row.Slot) - row.IntroOffset);
                // Độ hiện diện lẻ = row đang trượt ra từ sau bục: hiện dần theo đúng nhịp nó di chuyển. Cờ tắt thì luôn 1.
                float presence = _model.ListPresence(row);
                view.Render(row, position, row.Scale * row.IntroScale, row.IntroAlpha * presence, row.Lift, _context);
            }

            if (hasActivatedView) OrderLocalRowOnTop();
            if (sunburst && sunburst.IsPlaying) PositionSunburst();
            UpdateSticky(deltaTime, viewTop, viewBottom);
        }

        private float AnchoredFollowTarget(RowState localRow, float centeredTarget)
        {
            if (float.IsNaN(_anchorStartSlot))
            {
                _anchorStartSlot = localRow.Slot;
                _anchorStartScroll = centeredTarget;
            }

            int finalIndex = _model.IndexOf(localRow);
            if (finalIndex < 0) return centeredTarget;
            float finalSlot = finalIndex;

            // "Đỉnh là nhà": row mình đáp lên phần host trình bày (bục) thì đích của camera là ĐỈNH list (cuộn 0, cả bục
            // lộ ra) ngay khi row chạm ranh giới — không phải canh giữa ô đích nằm sau bục. Canh giữa ô 2 hay 3 là cắt mất
            // nửa trên của bục đúng lúc host bắt đầu diễn cú lên bục trên đó.
            bool isHomeAtTop = _model.LocalLandsOnPodium;
            if (isHomeAtTop)
            {
                finalSlot = _model.HiddenLeadingSlots;
                // Bắt đầu ở ngay ranh giới hoặc đã trên bục: không còn quãng nào để nội suy — camera đã ở nhà từ SetModel.
                if (_anchorStartSlot <= finalSlot + 0.0001f) return 0f;
            }

            float span = finalSlot - _anchorStartSlot;
            if (Mathf.Abs(span) < 0.0001f) return centeredTarget;

            float finalScroll = isHomeAtTop ? 0f : Layout.CenteredScroll(finalSlot, ViewportHeight, MaximumScroll);
            float progress = Mathf.Clamp01((localRow.Slot - _anchorStartSlot) / span);
            return Mathf.Lerp(_anchorStartScroll, finalScroll, progress);
        }

        /// <summary>
        /// Camera coi ĐỈNH list là nhà của row mình: khi nó đang nằm sau bục (ô &lt; ranh giới), hoặc sẽ đáp lên bục và đã tới
        /// ranh giới. Cờ <c>HostPresentedTopRanks</c> tắt thì luôn false — mọi đường camera y như cũ.
        /// </summary>
        private bool IsCameraHomeAtTop(RowState localRow)
        {
            int hiddenLeadingSlots = _model.HiddenLeadingSlots;
            if (hiddenLeadingSlots <= 0 || localRow == null) return false;
            if (localRow.Slot < hiddenLeadingSlots) return true;
            return _model.LocalLandsOnPodium && localRow.Slot <= hiddenLeadingSlots + 0.0001f;
        }

        /// <summary>Chỗ cuộn "đứng ở row mình": canh giữa row, hoặc đỉnh list khi camera ở nhà (<see cref="IsCameraHomeAtTop"/>).</summary>
        private float LocalRowScrollTarget(RowState localRow)
        {
            return IsCameraHomeAtTop(localRow) ? 0f : Layout.CenteredScroll(localRow.Slot, ViewportHeight, MaximumScroll);
        }

        private void UpdateCamera(float deltaTime)
        {
            RowState localRow = _model.LocalRow;
            if (_isSnapRequested)
            {
                _isSnapRequested = false;
                if (localRow != null)
                {
                    StopSmoothScroll();
                    _followVelocity = 0f;
                    SetScrollY(LocalRowScrollTarget(localRow));
                    return;
                }
            }

            if (FollowLocalRow && localRow != null)
            {
                if (_model.HasPodiumApproach && !float.IsNaN(_model.PodiumApproachStartScroll))
                {
                    // Cú tiếp cận bục kiểu cuộn: camera đi theo CÙNG tiến độ đã ease mà timeline vừa dùng cho Slot của row mình
                    // (không bám mềm, không kẹp theo slot) — trên màn hình row đi đúng đường thẳng từ chỗ canh giữa tới ô ranh
                    // giới, như danh sách và thẻ nổi của bản tham chiếu cùng chạy một đường cong từ cùng một frame. Trước cú tiếp
                    // cận tiến độ là 0 (đứng ở chỗ canh giữa lúc mở màn), sau đó là 1 (đỉnh list — "nhà" của màn lên bục).
                    _followVelocity = 0f;
                    SetScrollY(Mathf.LerpUnclamped(_model.PodiumApproachStartScroll, _model.PodiumApproachEndScroll,
                                                   _model.PodiumApproachProgress));
                    return;
                }

                float target = Layout.CenteredScroll(localRow.Slot, ViewportHeight, MaximumScroll);

                // Bám mềm cũng phải về "nhà" khi row mình đáp lên bục: lấy đích của bám neo (tới cuộn 0 đúng lúc chạm ranh giới)
                // làm đích để SmoothDamp đuổi theo. Cờ tắt thì LocalLandsOnPodium luôn false — đường bám mềm y như cũ.
                if (_context.Motion.FollowSmoothTime > 0f && _model.LocalLandsOnPodium) target = AnchoredFollowTarget(localRow, target);

                // FollowSmoothTime <= 0 = bám CỨNG: row mình là điểm cố định tuyệt đối trên màn, danh sách cuộn
                // dưới chân nó. Trước đây 0 bị SmoothDamping kẹp lên 0,0001 nên vẫn là bám mềm — mà bám mềm thì
                // ở đỉnh tốc độ của cú leo (InOutCubic đạt 3× tốc độ trung bình) row mình sụt xuống rồi trồi lên
                // cả một hàng, đúng thứ clip tham chiếu không có: đo trên phim của ta, tâm row đi 399 → 325 → 391.
                if (_context.Motion.FollowSmoothTime <= 0f)
                {
                    // BÁM NEO: camera nội suy tuyến tính THEO SLOT giữa chỗ cuộn lúc bắt đầu và chỗ cuộn khi row về chỗ
                    // cuối — thay vì "canh giữa row rồi kẹp vào biên". Kẹp là phi tuyến: row ở gần đáy bảng (hạng 48/50)
                    // không canh giữa được, nên vừa leo được một chút là biên nhả ra và row giật lên cả khúc trong
                    // ~0,1 s đầu. Nội suy theo slot thì độ lệch đó trải đều suốt cú leo — đúng như clip tham chiếu,
                    // nơi hàng người chơi trôi đều 54 px trong 0,95 s chứ không giật.
                    _followVelocity = 0f;
                    SetScrollY(AnchoredFollowTarget(localRow, target));
                }
                else if (deltaTime <= 0f)
                {
                    _followVelocity = 0f;
                    SetScrollY(target);
                }
                else
                {
                    SetScrollY(SmoothDamping.Step(GetScrollY(), target, ref _followVelocity, _context.Motion.FollowSmoothTime,
                                                  float.PositiveInfinity, deltaTime));
                }
                return;
            }

            if (_isSmoothScrolling)
            {
                _smoothScrollElapsed += deltaTime;
                float progress = Mathf.Clamp01(_smoothScrollElapsed / _smoothScrollDuration);
                SetScrollY(Mathf.LerpUnclamped(_smoothScrollFrom, _smoothScrollTo, Easing.InOutCubic(progress)));
                if (progress >= 1f) _isSmoothScrolling = false;
            }
        }

        private void UpdateSticky(float deltaTime, float viewTop, float viewBottom)
        {
            if (stickyAnchor == null || stickyGroup == null) return;
            RowState localRow = _model.LocalRow;

            int wantedSide = 0;
            // Row mình do host trình bày (đang sau bục, hoặc sẽ đáp lên bục) thì không có "thanh của bạn" nào để dính mép: người
            // chơi đang ở trên bục, một thanh hạng 1–3 dính ở mép list là nói sai chỗ của họ.
            bool isLocalRowPresentedByHost = localRow != null && (_model.IsPresentedByHost(localRow) || _model.LocalLandsOnPodium);
            if (localRow != null && !FollowLocalRow && !ForceHideSticky && !isLocalRowPresentedByHost)
            {
                float rowTop = Layout.SlotToTop(localRow.Slot);
                if (rowTop + rowHeight * stickyShowFraction < viewTop) wantedSide = -1;
                else if (rowTop + rowHeight * stickyHideFraction > viewBottom) wantedSide = 1;
            }

            if (wantedSide != 0 && wantedSide != _stickySide)
            {
                _stickySide = wantedSide;
                float anchorY = wantedSide < 0 ? 1f : 0f;
                stickyAnchor.anchorMin = new Vector2(stickyAnchor.anchorMin.x, anchorY);
                stickyAnchor.anchorMax = new Vector2(stickyAnchor.anchorMax.x, anchorY);
                stickyAnchor.pivot = new Vector2(0.5f, anchorY);
            }

            float step = (deltaTime <= 0f ? 1f : deltaTime) * stickyFadeSpeed;
            _stickyVisibility = Mathf.MoveTowards(_stickyVisibility, wantedSide != 0 ? 1f : 0f, step);
            if (_stickyVisibility <= 0f)
            {
                if (stickyAnchor.gameObject.activeSelf) stickyAnchor.gameObject.SetActive(false);
                return;
            }
            if (!stickyAnchor.gameObject.activeSelf) stickyAnchor.gameObject.SetActive(true);

            if (_stickyView != null && localRow != null)
            {
                if (!ReferenceEquals(_stickyView.BoundRow, localRow) || _stickyView.BoundContentVersion != localRow.ContentVersion)
                {
                    _stickyView.Bind(localRow, _context);
                }
                _stickyView.Render(localRow, Vector2.zero, 1f, 1f, 0f, _context);
            }

            float eased = Easing.OutCubic(_stickyVisibility);
            float slide = (1f - eased) * rowHeight * stickySlideFraction;
            float positionY = _stickySide < 0 ? -stickyInset + slide : stickyInset - slide;
            stickyAnchor.anchoredPosition = new Vector2(stickyAnchor.anchoredPosition.x, positionY);
            stickyGroup.alpha = eased;
            stickyGroup.blocksRaycasts = eased > 0.5f;
        }

        // ---------------------------------------------------------------- Tiện ích

        private void EnsureInitialized()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            if (rowPrefab != null)
            {
                for (int index = 0; index < prewarmRowCount; index++) ReleaseView(CreateView(content));
                if (stickyAnchor != null)
                {
                    _stickyView = CreateView(stickyAnchor);
                    RectTransform stickyTransform = _stickyView.RectTransform;
                    stickyTransform.anchorMin = Vector2.zero;
                    stickyTransform.anchorMax = Vector2.one;
                    stickyTransform.pivot = new Vector2(0.5f, 0.5f);
                    stickyTransform.offsetMin = Vector2.zero;
                    stickyTransform.offsetMax = Vector2.zero;
                    _stickyView.gameObject.SetActive(true);
                }
            }
            if (stickyButton != null) stickyButton.onClick.AddListener(ScrollToLocalRow);
            HideStickyImmediately();
        }

        private void HideStickyImmediately()
        {
            _stickyVisibility = 0f;
            _stickySide = 0;
            if (stickyGroup != null)
            {
                stickyGroup.alpha = 0f;
                stickyGroup.blocksRaycasts = false;
            }
            if (stickyAnchor != null && stickyAnchor.gameObject.activeSelf) stickyAnchor.gameObject.SetActive(false);
        }

        private void RebuildRowSet()
        {
            _rowsInModel.Clear();
            if (_model != null)
            {
                IReadOnlyList<RowState> rows = _model.Rows;
                for (int index = 0; index < rows.Count; index++) _rowsInModel.Add(rows[index]);
            }
            RefreshContentHeight();
            _knownStructureVersion = _model != null ? _model.StructureVersion : -1;
        }

        private void RefreshContentHeight()
        {
            if (!content) return;
            ResolveDividers();
            float height = _model != null ? Layout.ContentHeight(_model.MaximumSlot) : 0f;
            if (Mathf.Abs(content.sizeDelta.y - height) > 0.01f) content.sizeDelta = new Vector2(content.sizeDelta.x, height);
            SetScrollY(GetScrollY());
        }

        /// <summary>
        /// Quy hạng ranh giới ra slot theo bảng cuối cùng (<see cref="BoardScene.Rows"/>: index = slot lúc đứng yên), rồi đặt view.
        /// Dải nằm dưới mọi row (sibling đầu) để row đang leo lướt ĐÈ lên dải chứ không chui xuống.
        /// </summary>
        private void ResolveDividers()
        {
            _resolvedDividers.Clear();
            _resolvedDividerViews.Clear();
            if (_rankDividers.Count == 0) return;

            IReadOnlyList<BoardRow> sceneRows = _model != null ? _model.Scene.Rows : null;
            float maximumSlot = _model != null ? _model.MaximumSlot : -1f;

            for (int index = 0; index < _rankDividers.Count; index++)
            {
                LeaderboardRankDivider divider = _rankDividers[index];
                int slot = sceneRows != null ? SlotOfRank(sceneRows, divider.BeforeRank) : -1;
                // Đuôi bảng đang bị cắt tạm lúc diễn thì dải nằm ngoài phần đang có cũng ẩn theo.
                bool isVisible = slot >= 0 && slot <= maximumSlot && !ContainsSlot(slot);
                if (divider.View != null && divider.View.gameObject.activeSelf != isVisible) divider.View.gameObject.SetActive(isVisible);
                if (!isVisible) continue;

                int insertAt = _resolvedDividers.Count;
                while (insertAt > 0 && _resolvedDividers[insertAt - 1].BeforeSlot > slot) insertAt--;
                _resolvedDividers.Insert(insertAt, new ListDivider(slot, divider.Height));
                _resolvedDividerViews.Insert(insertAt, divider.View);
            }

            VirtualListLayout layout = Layout;
            for (int index = _resolvedDividerViews.Count - 1; index >= 0; index--)
            {
                RectTransform view = _resolvedDividerViews[index];
                if (view == null) continue;
                view.anchorMin = new Vector2(view.anchorMin.x, 1f);
                view.anchorMax = new Vector2(view.anchorMax.x, 1f);
                float height = _resolvedDividers[index].Height;
                float top = layout.DividerTop(index);
                view.sizeDelta = new Vector2(view.sizeDelta.x, height);
                view.anchoredPosition = new Vector2(view.anchoredPosition.x, -(top + height * (1f - view.pivot.y)));
                view.SetAsFirstSibling();
            }
        }

        private bool ContainsSlot(int slot)
        {
            for (int index = 0; index < _resolvedDividers.Count; index++)
            {
                if (_resolvedDividers[index].BeforeSlot == slot) return true;
            }
            return false;
        }

        private static int SlotOfRank(IReadOnlyList<BoardRow> rows, int rank)
        {
            for (int index = 0; index < rows.Count; index++)
            {
                BoardRow row = rows[index];
                if (row.IsGap || row.Entry == null) continue;
                if (row.Entry.Rank == rank) return index;
                if (row.Entry.Rank > rank) return -1;
            }
            return -1;
        }

        /// <summary>
        /// View của row mình đang được vẽ ở lớp nổi (ngoài mask): prefab có <c>floatingRowLayer</c> VÀ màn diễn của model có cú
        /// tiếp cận bục kiểu cuộn. Thiếu một trong hai thì mọi view ở dưới Content như cũ.
        ///
        /// <para>Vì sao cần: ô ranh giới cách mép trên khung nhìn một khoảng cố định (vùng bục + nửa hàng), nên trên màn thấp (4:3,
        /// khung nhìn chỉ ~550) nó nằm DƯỚI đáy khung nhìn — mask cắt mất row mình ở cuối cú tiếp cận. Bản tham chiếu vẽ tấm thẻ
        /// của người chơi thành một lớp nổi riêng, không bị mask, và nó trôi qua cả dải nút.</para>
        /// </summary>
        private bool IsLocalRowFloating => floatingRowLayer != null && _model != null && _model.HasPodiumApproach;

        /// <summary>
        /// Đặt <paramref name="layer"/> trùng khít khung Content trong không gian thế giới (neo điểm, cùng pivot, cùng cỡ, cùng
        /// vị trí / góc / tỉ lệ). View đổi cha sang lớp này giữ nguyên <c>anchoredPosition</c> / <c>localScale</c> mà vẫn đứng
        /// đúng chỗ list sẽ đặt nó dưới Content. Chỉ ghi transform khi giá trị đổi.
        /// </summary>
        private void MirrorContentOnto(RectTransform layer)
        {
            var center = new Vector2(0.5f, 0.5f);
            if (layer.anchorMin != center) layer.anchorMin = center;
            if (layer.anchorMax != center) layer.anchorMax = center;
            if (layer.pivot != content.pivot) layer.pivot = content.pivot;
            Vector2 size = content.rect.size;
            if (layer.sizeDelta != size) layer.sizeDelta = size;

            Transform parent = layer.parent;
            Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;
            Vector3 contentScale = content.lossyScale;
            var scale = new Vector3(ScaleRatio(contentScale.x, parentScale.x), ScaleRatio(contentScale.y, parentScale.y), 1f);
            if (layer.localScale != scale) layer.localScale = scale;
            if (layer.rotation != content.rotation) layer.rotation = content.rotation;
            if (layer.position != content.position) layer.position = content.position;
        }

        /// <summary>Tỉ lệ cần cho lớp nổi; cha đang có tỉ lệ 0 (popup chưa nở) thì giữ 1 thay vì chia cho 0.</summary>
        private static float ScaleRatio(float target, float parent)
        {
            return Mathf.Abs(parent) < 0.000001f ? 1f : target / parent;
        }

        private static void PlaceUnderParent(LeaderboardEntryView view, Transform parent)
        {
            if (view.transform.parent != parent) view.transform.SetParent(parent, false);
        }

        private void OrderLocalRowOnTop()
        {
            if (sunburst && sunburst.IsPlaying) sunburst.transform.SetAsLastSibling();
            if (_model?.LocalRow != null && _activeViews.TryGetValue(_model.LocalRow, out LeaderboardEntryView view)) view.transform.SetAsLastSibling();
        }

        private void PositionSunburst()
        {
            if (_model?.LocalRow == null) return;
            sunburst.RectTransform.anchoredPosition = new Vector2(0f, -Layout.SlotToCenter(_model.LocalRow.Slot));
        }

        private float GetScrollY()
        {
            return content ? content.anchoredPosition.y : 0f;
        }

        private void SetScrollY(float scroll)
        {
            if (!content) return;
            scroll = Mathf.Clamp(scroll, 0f, MaximumScroll);
            Vector2 position = content.anchoredPosition;
            if (Mathf.Abs(position.y - scroll) > 0.01f) content.anchoredPosition = new Vector2(position.x, scroll);
        }

        private void StopSmoothScroll()
        {
            _isSmoothScrolling = false;
        }

        private LeaderboardEntryView CreateView(Transform parent)
        {
            LeaderboardEntryView view = Instantiate(rowPrefab, parent, false);
            view.name = rowPrefab.name;
            return view;
        }

        private LeaderboardEntryView AcquireView()
        {
            LeaderboardEntryView view = _pool.Count > 0 ? _pool.Pop() : CreateView(content);
            if (!view.gameObject.activeSelf) view.gameObject.SetActive(true);
            return view;
        }

        private void ReleaseView(LeaderboardEntryView view)
        {
            view.Unbind();
            view.gameObject.SetActive(false);
            // View đang ở lớp nổi (row mình trong cú tiếp cận bục) về lại Content trước khi vào pool — pool chỉ giữ view của
            // Content, và lần dùng sau có thể là một row thường.
            PlaceUnderParent(view, content);
            _pool.Push(view);
        }

        private void ReleaseAllViews()
        {
            foreach (KeyValuePair<RowState, LeaderboardEntryView> pair in _activeViews) ReleaseView(pair.Value);
            _activeViews.Clear();
        }
    }
}
