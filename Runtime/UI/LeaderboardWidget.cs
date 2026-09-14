using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamTech.Leaderboard.ViewModel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>
    /// Khối leaderboard lắp được vào bất kỳ host nào (popup, màn riêng, một phần layout màn Win).
    ///
    /// <para><b>Hợp đồng với host</b></para>
    /// <list type="number">
    /// <item><see cref="Arm"/> ngay trước khi host bắt đầu hiện (trước animation mở): xoá nội dung cũ để lúc pop-in không lộ row lần trước.</item>
    /// <item><see cref="PresentAsync"/>: tải dữ liệu NGAY, nhưng chỉ bắt đầu intro khi <see cref="LeaderboardPresentRequest.HostReady"/> xong.</item>
    /// <item><see cref="Disarm"/> khi host bắt đầu ẩn: dừng diễn (ghi nhận đã xem nếu đã chạm nhịp hạ cánh), giữ nguyên hình cho lúc fade.</item>
    /// </list>
    /// Widget không làm gì trong OnEnable (host kiểu popup của kit không bao giờ tắt GameObject) và không tự tra board —
    /// host truyền board vào, nên mọi phụ thuộc đều nhìn thấy được.
    /// </summary>
    [DefaultExecutionOrder(110)]
    [DisallowMultipleComponent]
    public sealed class LeaderboardWidget : MonoBehaviour, IRevealListener
    {
        [Header("Thành phần")]
        [SerializeField] private LeaderboardScrollView scrollView;
        [SerializeField] private LeaderboardStatusView statusView;
        [SerializeField] private TopTierBannerView bannerView;
        [SerializeField] private CelebrationBurstView celebrationView;
        [Tooltip("Vùng banner được phép nằm trong (thường là viewport của list).")]
        [SerializeField] private RectTransform bannerBounds;
        [Tooltip("Lớp phủ trong suốt: chạm = skip. Chỉ bật trong lúc diễn.")]
        [SerializeField] private Button skipCatcher;
        [SerializeField] private TMP_Text titleText;

        [Header("Cấu hình (để trống = mặc định)")]
        [SerializeField] private LeaderboardMotionConfig motionConfig;
        [SerializeField] private LeaderboardThemeConfig themeConfig;
        [SerializeField] private LeaderboardTextConfig textConfig;

        [Header("Phản hồi")]
        [Tooltip("Component cài ILeaderboardFeedbackSink trên prefab (âm thanh, UnityEvent...). Host có thể thêm sink qua code.")]
        [SerializeField] private List<MonoBehaviour> feedbackSinkComponents = new List<MonoBehaviour>();

        private readonly List<ILeaderboardFeedbackSink> _hostSinks = new List<ILeaderboardFeedbackSink>();
        private readonly List<ILeaderboardFeedbackSink> _activeSinks = new List<ILeaderboardFeedbackSink>();

        private bool _isInitialized;
        private bool _isArmed;
        private int _presentGeneration;
        private TaskCompletionSource<LeaderboardPresentResult> _presentCompletion;
        private CancellationTokenSource _presentCancellation;
        private LeaderboardPresentRequest _lastRequest;
        private LeaderboardRenderContext _context;
        private MotionSettings _motion;
        private BoardModel _model;
        private RevealTimeline _timeline;
        private IDisposable _revealLease;
        private float _debugTimeScale = 1f;

        public event Action<LeaderboardPresentResult> PresentFinished;

        public bool IsArmed => _isArmed;
        public bool IsPresenting => _presentCompletion != null;
        public LeaderboardScrollView ScrollView => scrollView;
        internal BoardModel CurrentModel => _model;
        internal RevealTimeline CurrentTimeline => _timeline;
        internal LeaderboardStatusView StatusView => statusView;
        internal TopTierBannerView BannerView => bannerView;
        internal CelebrationBurstView CelebrationView => celebrationView;
        internal Button SkipCatcher => skipCatcher;

        /// <summary>Làm chậm/nhanh toàn bộ animation (debug, chụp màn hình). 1 = bình thường.</summary>
        public float DebugTimeScale
        {
            get => _debugTimeScale;
            set
            {
                _debugTimeScale = Mathf.Max(0f, value);
                ApplyTimeScale();
            }
        }

        /// <summary>Host thêm sink riêng (vd audio stack + haptic của game), cộng với các sink component trên prefab.</summary>
        public void SetFeedbackSinks(IReadOnlyList<ILeaderboardFeedbackSink> sinks)
        {
            _hostSinks.Clear();
            if (sinks != null)
            {
                for (int index = 0; index < sinks.Count; index++)
                {
                    if (sinks[index] != null) _hostSinks.Add(sinks[index]);
                }
            }
            RebuildActiveSinks();
        }

        public void Arm()
        {
            EnsureInitialized();
            CancelPresent(PresentOutcome.Cancelled);
            if (scrollView) scrollView.Clear();
            if (bannerView) bannerView.Hide();
            if (celebrationView) celebrationView.Clear();
            if (statusView) statusView.HideAll();
            SetSkipCatcherActive(false);
            _model = null;
            _timeline = null;
            _isArmed = true;
        }

        public Task<LeaderboardPresentResult> PresentAsync(LeaderboardPresentRequest request, CancellationToken cancellationToken)
        {
            if (request.Board == null) throw new ArgumentException("Request phải có board.", nameof(request));
            if (!_isArmed) Arm();
            else CancelPresent(PresentOutcome.Cancelled);

            int generation = ++_presentGeneration;
            _lastRequest = request;
            var completion = new TaskCompletionSource<LeaderboardPresentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _presentCompletion = completion;
            _presentCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            RunPresent(request, generation, _presentCancellation.Token);
            return completion.Task;
        }

        public void Skip()
        {
            _timeline?.RequestSkip();
        }

        public void Disarm()
        {
            if (!_isArmed) return;
            CancelPresent(PresentOutcome.Cancelled);
            SetSkipCatcherActive(false);
            _isArmed = false;
        }

        /// <summary>Tua thời gian bằng tay (test EditMode không có Update/LateUpdate).</summary>
        internal void AdvanceForTests(float deltaTime)
        {
            Tick(deltaTime);
            if (scrollView) scrollView.RefreshForTests(deltaTime);
        }

        private void Update()
        {
            if (!_isArmed || _model == null) return;
            Tick(Time.unscaledDeltaTime * _debugTimeScale);
        }

        private void LateUpdate()
        {
            if (bannerView && bannerView.IsVisible) PositionBanner();
        }

        private void OnDestroy()
        {
            CancelPresent(PresentOutcome.Cancelled);
        }

        // ---------------------------------------------------------------- Luồng trình bày

        private async void RunPresent(LeaderboardPresentRequest request, int generation, CancellationToken cancellationToken)
        {
            try
            {
                LeaderboardMotionConfig motion = motionConfig ? motionConfig : RuntimeDefaults.Motion;
                _motion = motion.CreateSettings();
                _context = new LeaderboardRenderContext(_motion, motion.Visuals, themeConfig ? themeConfig : RuntimeDefaults.Theme,
                                                        textConfig ? textConfig : RuntimeDefaults.Text, request.Board.TierRule);
                if (titleText && titleText.text != _context.Text.Title) titleText.text = _context.Text.Title;
                if (scrollView) scrollView.Configure(_context);
                if (statusView) statusView.ShowLoading(_context.Text.Loading, _context.Visuals.LoadingIndicatorDelay, _context.Visuals.LoadingDotsPerSecond);

                Task<BoardScene> loadTask = request.Board.LoadSceneAsync(request.Mode, cancellationToken);
                Task hostReadyTask = WaitForHostReadyAsync(request.HostReady, _context.Visuals.HostReadyTimeout, cancellationToken);
                await Task.WhenAll(loadTask, hostReadyTask);

                if (this == null || generation != _presentGeneration) return;
                cancellationToken.ThrowIfCancellationRequested();
                BeginShow(request, loadTask.Result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (this != null && generation == _presentGeneration) CompletePresent(PresentOutcome.Cancelled, null);
            }
            catch (Exception exception)
            {
                // Gồm cả OperationCanceledException KHÔNG đến từ token của lượt trình bày (adapter / dịch vụ ném huỷ lạc, vd bỏ lượt gọi
                // cũ sau khi xoá dữ liệu): không ai huỷ lượt này, coi là huỷ thì widget đứng mãi ở "Loading" không có nút thử lại.
                // Lượt đã bị lượt mới thay thế thì vẫn thoát êm ở dòng dưới.
                if (this == null || generation != _presentGeneration) return;
                Debug.LogWarning("[Leaderboard] Không trình bày được board '" + request.Board.BoardId + "': " + exception.Message, this);
                if (statusView && _context != null) statusView.ShowError(_context.Text.ErrorMessage, _context.Text.Retry);
                CompletePresent(PresentOutcome.Failed, exception);
            }
        }

        private static Task WaitForHostReadyAsync(Task hostReady, float timeoutSeconds, CancellationToken cancellationToken)
        {
            if (hostReady == null || hostReady.IsCompleted) return Task.CompletedTask;
            return WaitWithTimeoutAsync(hostReady, timeoutSeconds, cancellationToken);
        }

        private static async Task WaitWithTimeoutAsync(Task hostReady, float timeoutSeconds, CancellationToken cancellationToken)
        {
            Task timeout = Task.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, timeoutSeconds)), cancellationToken);
            await Task.WhenAny(hostReady, timeout);
            cancellationToken.ThrowIfCancellationRequested();
        }

        private void BeginShow(LeaderboardPresentRequest request, BoardScene scene)
        {
            if (statusView) statusView.HideAll();
            if (scene.Rows.Count == 0)
            {
                if (statusView) statusView.ShowEmpty(_context.Text.Empty);
                CompletePresent(PresentOutcome.Completed, null);
                return;
            }

            _model = new BoardModel(scene, _motion);
            bool shouldReveal = scene.NeedsReveal && request.Board.TryBeginReveal(out _revealLease);
            if (!scrollView)
            {
                CompletePresent(PresentOutcome.Completed, null);
                return;
            }

            if (shouldReveal)
            {
                _timeline = new RevealTimeline(_model, this);
                _timeline.Prepare();
                scrollView.PinLocalRow = true;
                scrollView.FollowLocalRow = true;
                scrollView.SetUserScroll(false);
                scrollView.SetModel(_model, true);
                SetSkipCatcherActive(true);
                _timeline.Start();
            }
            else
            {
                _timeline = null;
                scrollView.PinLocalRow = false;
                scrollView.FollowLocalRow = false;
                scrollView.SetUserScroll(true);
                scrollView.SetModel(_model, true);
                CompletePresent(PresentOutcome.Completed, null);
            }
        }

        private void Tick(float deltaTime)
        {
            if (_model == null) return;
            if (_timeline != null && !_timeline.IsFinished)
            {
                try
                {
                    _timeline.Tick(deltaTime);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Leaderboard] Màn diễn lỗi, kết thúc ngay: " + exception.Message, this);
                    _timeline.ForceFinish();
                    FinishReveal(PresentOutcome.Failed, exception);
                }
            }
            _model.Advance(deltaTime);
            if (_timeline != null && _timeline.IsFinished && _presentCompletion != null)
            {
                FinishReveal(_timeline.WasSkipped ? PresentOutcome.Skipped : PresentOutcome.Completed, null);
            }
        }

        private void FinishReveal(PresentOutcome outcome, Exception error)
        {
            if (_timeline != null && _timeline.HasReachedLanding) _lastRequest.Board?.MarkRevealed(_timeline.Change);
            ReleaseRevealLease();
            if (scrollView)
            {
                scrollView.FollowLocalRow = false;
                scrollView.PinLocalRow = false;
                scrollView.SetUserScroll(true);
            }
            SetSkipCatcherActive(false);
            CompletePresent(outcome, error);
        }

        private void CancelPresent(PresentOutcome outcome)
        {
            _presentGeneration++;
            if (_presentCancellation != null)
            {
                _presentCancellation.Cancel();
                _presentCancellation.Dispose();
                _presentCancellation = null;
            }
            if (_timeline != null && !_timeline.IsFinished)
            {
                if (_timeline.HasReachedLanding) _lastRequest.Board?.MarkRevealed(_timeline.Change);
                _timeline.ForceFinish();
            }
            ReleaseRevealLease();
            if (scrollView)
            {
                scrollView.FollowLocalRow = false;
                scrollView.PinLocalRow = false;
            }
            if (_presentCompletion != null) CompletePresent(outcome, null);
        }

        private void CompletePresent(PresentOutcome outcome, Exception error)
        {
            TaskCompletionSource<LeaderboardPresentResult> completion = _presentCompletion;
            if (completion == null) return;
            _presentCompletion = null;
            RankChange change = _model != null ? _model.Scene.Change : default;
            var result = new LeaderboardPresentResult(outcome, change, error);
            completion.TrySetResult(result);
            PresentFinished?.Invoke(result);
        }

        private void ReleaseRevealLease()
        {
            _revealLease?.Dispose();
            _revealLease = null;
        }

        // ---------------------------------------------------------------- IRevealListener

        void IRevealListener.OnBeat(LeaderboardBeat beat, in LeaderboardBeatContext context)
        {
            for (int index = 0; index < _activeSinks.Count; index++)
            {
                try
                {
                    _activeSinks[index].OnBeat(beat, context);
                }
                catch (Exception exception)
                {
                    // Một sink hỏng không được làm hỏng màn diễn.
                    Debug.LogWarning("[Leaderboard] Feedback sink lỗi ở nhịp " + beat + ": " + exception.Message, this);
                }
            }
        }

        void IRevealListener.OnLanded(RankTier tier, RowState localRow)
        {
            if (!celebrationView || !scrollView) return;
            if (!scrollView.TryGetRowBounds(localRow, celebrationView.RectTransform, out Rect bounds)) return;
            int bonus = (RankTier.Standard - tier) * _context.Visuals.TwinkleBonusPerTier;
            celebrationView.Twinkles(bounds, _context.Visuals.TwinkleCount + bonus, _context.Theme.TwinkleColor, _context.Visuals);
        }

        void IRevealListener.OnCelebrate(RankTier tier, RowState localRow)
        {
            LeaderboardVisualSettings visuals = _context.Visuals;
            Color color = _context.Theme.TierColor(tier);
            if (scrollView)
            {
                scrollView.PlaySunburst(color, tier == RankTier.FirstPlace ? visuals.SunburstFirstPlaceScale : visuals.SunburstPodiumScale, visuals);
            }
            if (bannerView && localRow.Entry != null)
            {
                string title = tier == RankTier.FirstPlace ? _context.Text.FirstPlaceTitle : _context.Text.PodiumTitle;
                string subtitle = string.Format(_context.Text.RankFormat, localRow.Entry.OneBasedRank);
                bannerView.Show(title, subtitle, color, visuals);
                PositionBanner();
            }
            if (celebrationView && scrollView && scrollView.TryGetRowBounds(localRow, celebrationView.RectTransform, out Rect bounds))
            {
                int count = tier == RankTier.FirstPlace ? visuals.ConfettiFirstPlace : visuals.ConfettiPodium;
                if (count > 0) celebrationView.Confetti(bounds, count, _context.Theme.ConfettiPalette, visuals);
            }
        }

        void IRevealListener.OnTailRevealed(IReadOnlyList<RowState> tail)
        {
            // List tự thấy StructureVersion đổi và tính lại chiều cao nội dung ở lần refresh kế tiếp.
        }

        void IRevealListener.OnCameraSnapRequested()
        {
            if (scrollView) scrollView.RequestSnapToLocalRow();
        }

        // ---------------------------------------------------------------- Tiện ích

        private void EnsureInitialized()
        {
            if (_isInitialized) return;
            _isInitialized = true;
            if (skipCatcher) skipCatcher.onClick.AddListener(Skip);
            if (statusView) statusView.RetryClicked += Retry;
            RebuildActiveSinks();
            ApplyTimeScale();
        }

        private void Retry()
        {
            if (_lastRequest.Board == null) return;
            var request = new LeaderboardPresentRequest(_lastRequest.Board, _lastRequest.Mode, null);
            _ = PresentAsync(request, CancellationToken.None);
        }

        private void RebuildActiveSinks()
        {
            _activeSinks.Clear();
            if (feedbackSinkComponents != null)
            {
                for (int index = 0; index < feedbackSinkComponents.Count; index++)
                {
                    if (feedbackSinkComponents[index] is ILeaderboardFeedbackSink sink) _activeSinks.Add(sink);
                }
            }
            _activeSinks.AddRange(_hostSinks);
        }

        private void PositionBanner()
        {
            if (_model == null || _model.LocalRow == null || !scrollView) return;
            var bannerParent = bannerView.Body ? bannerView.Body.parent as RectTransform : null;
            if (bannerParent == null) return;
            if (!scrollView.TryGetRowBounds(_model.LocalRow, bannerParent, out Rect rowBounds)) return;

            // Đổi sang trục Y hướng xuống, gốc = mép trên vùng chứa banner.
            Rect parentRect = bannerParent.rect;
            float rowTop = parentRect.yMax - rowBounds.yMax;
            float rowBottom = parentRect.yMax - rowBounds.yMin;
            float containerTop = 0f;
            float containerBottom = parentRect.height;
            if (bannerBounds && RectTransformBounds.TryGetBounds(bannerBounds, bannerParent, out Rect limitBounds))
            {
                containerTop = parentRect.yMax - limitBounds.yMax;
                containerBottom = parentRect.yMax - limitBounds.yMin;
            }

            BannerPlacement placement = BannerPlacement.Resolve(rowTop, rowBottom, bannerView.BodyHeight, containerTop, containerBottom,
                                                                _context.Visuals.BannerGap);
            float localCenterY = parentRect.yMax - placement.CenterY;
            RectTransform body = bannerView.Body;
            float anchorReferenceY = Mathf.Lerp(parentRect.yMin, parentRect.yMax, body.anchorMin.y);
            float pivotOffset = (body.pivot.y - 0.5f) * bannerView.BodyHeight;
            bannerView.SetCenterY(localCenterY - anchorReferenceY + pivotOffset);
        }

        private void SetSkipCatcherActive(bool isActive)
        {
            if (skipCatcher && skipCatcher.gameObject.activeSelf != isActive) skipCatcher.gameObject.SetActive(isActive);
        }

        private void ApplyTimeScale()
        {
            if (scrollView) scrollView.TimeScale = _debugTimeScale;
            if (bannerView) bannerView.TimeScale = _debugTimeScale;
            if (celebrationView) celebrationView.TimeScale = _debugTimeScale;
        }

        /// <summary>Config mặc định tạo lúc chạy khi prefab để trống — tránh null, không thay cho config thật.</summary>
        private static class RuntimeDefaults
        {
            private static LeaderboardMotionConfig _motion;
            private static LeaderboardThemeConfig _theme;
            private static LeaderboardTextConfig _text;

            public static LeaderboardMotionConfig Motion => _motion ? _motion : (_motion = Create<LeaderboardMotionConfig>());
            public static LeaderboardThemeConfig Theme => _theme ? _theme : (_theme = Create<LeaderboardThemeConfig>());
            public static LeaderboardTextConfig Text => _text ? _text : (_text = Create<LeaderboardTextConfig>());

            private static T Create<T>() where T : ScriptableObject
            {
                var instance = ScriptableObject.CreateInstance<T>();
                instance.hideFlags = HideFlags.DontSave;
                return instance;
            }
        }
    }
}
