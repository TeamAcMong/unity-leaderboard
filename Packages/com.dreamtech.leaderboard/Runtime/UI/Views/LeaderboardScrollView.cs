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

        /// <summary>Hệ số thời gian debug (chụp màn hình chậm). 1 = bình thường.</summary>
        public float TimeScale { get; set; } = 1f;

        public BoardModel Model => _model;
        public RectTransform Viewport => viewport;
        public RectTransform Content => content;
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
            EnsureInitialized();
            ReleaseAllViews();
            StopSmoothScroll();
            _model = model;
            _knownStructureVersion = -1;
            _followVelocity = 0f;
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
            SetScrollY(localRow != null ? Layout.CenteredScroll(localRow.Slot, ViewportHeight, MaximumScroll) : 0f);
            if (playIntro) _model.StartIntro(Layout.TopVisibleSlot(GetScrollY()));
            Refresh(0f);
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
            _smoothScrollTo = Layout.CenteredScroll(_model.LocalRow.Slot, ViewportHeight, MaximumScroll);
            _smoothScrollElapsed = 0f;
            _smoothScrollDuration = Mathf.Max(0.0001f, _context.Motion.ScrollToLocalDuration);
        }

        public void PlaySunburst(Color color, float scale, LeaderboardVisualSettings visuals)
        {
            if (!sunburst || _model == null || _model.LocalRow == null) return;
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
            float centerX = content.rect.center.x;

            Vector3 worldMinimum = content.TransformPoint(new Vector3(centerX - halfWidth, centerY - halfHeight, 0f));
            Vector3 worldMaximum = content.TransformPoint(new Vector3(centerX + halfWidth, centerY + halfHeight, 0f));
            Vector3 localMinimum = space.InverseTransformPoint(worldMinimum);
            Vector3 localMaximum = space.InverseTransformPoint(worldMaximum);
            bounds = Rect.MinMaxRect(Mathf.Min(localMinimum.x, localMaximum.x), Mathf.Min(localMinimum.y, localMaximum.y),
                                     Mathf.Max(localMinimum.x, localMaximum.x), Mathf.Max(localMinimum.y, localMaximum.y));
            return true;
        }

        internal LeaderboardEntryView GetActiveView(RowState row)
        {
            return row != null && _activeViews.TryGetValue(row, out LeaderboardEntryView view) ? view : null;
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
                if (!isStillInModel || (!isPinned && !layout.Intersects(row.Slot, viewTop - margin, viewBottom + margin))) _releaseBuffer.Add(row);
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
                bool isPinned = PinLocalRow && row == localRow;
                if (!isPinned && !layout.Intersects(row.Slot, viewTop - margin, viewBottom + margin)) continue;
                LeaderboardEntryView view = AcquireView();
                view.Bind(row, _context);
                _activeViews[row] = view;
                hasActivatedView = true;
            }

            foreach (KeyValuePair<RowState, LeaderboardEntryView> pair in _activeViews)
            {
                RowState row = pair.Key;
                LeaderboardEntryView view = pair.Value;
                if (!ReferenceEquals(view.BoundRow, row) || view.BoundContentVersion != row.ContentVersion) view.Bind(row, _context);
                var position = new Vector2(0f, -layout.SlotToCenter(row.Slot) - row.IntroOffset);
                view.Render(row, position, row.Scale * row.IntroScale, row.IntroAlpha, row.Lift, _context);
            }

            if (hasActivatedView) OrderLocalRowOnTop();
            if (sunburst && sunburst.IsPlaying) PositionSunburst();
            UpdateSticky(deltaTime, viewTop, viewBottom);
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
                    SetScrollY(Layout.CenteredScroll(localRow.Slot, ViewportHeight, MaximumScroll));
                    return;
                }
            }

            if (FollowLocalRow && localRow != null)
            {
                float target = Layout.CenteredScroll(localRow.Slot, ViewportHeight, MaximumScroll);
                if (deltaTime <= 0f)
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
            if (localRow != null && !FollowLocalRow && !ForceHideSticky)
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
            _pool.Push(view);
        }

        private void ReleaseAllViews()
        {
            foreach (KeyValuePair<RowState, LeaderboardEntryView> pair in _activeViews) ReleaseView(pair.Value);
            _activeViews.Clear();
        }
    }
}
