using System.Threading;
using System.Threading.Tasks;
using DreamTech.Leaderboard.UI;
using UnityEngine;

namespace DreamTech.Leaderboard.Demo
{
    /// <summary>
    /// Host mẫu cho <see cref="LeaderboardWidget"/>: một khung có animation mở/đóng. Ba host trong scene demo (màn riêng, popup,
    /// khối trong màn Win) dùng chung component này và chỉ khác bố cục — widget không biết mình đang nằm ở đâu.
    ///
    /// <para>Đúng hợp đồng host của package: <c>Arm</c> trước animation mở → <c>PresentAsync</c> với hostReady = mở xong →
    /// <c>Disarm</c> ngay khi bắt đầu đóng.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LeaderboardDemoHost : MonoBehaviour
    {
        [SerializeField] private string displayName = "Host";
        [SerializeField] private CanvasGroup group;
        [Tooltip("Phần được phóng to khi mở (khung popup, khối trong màn Win...).")]
        [SerializeField] private RectTransform panel;
        [SerializeField] private LeaderboardWidget widget;
        [SerializeField, Min(0f)] private float showDuration = 0.3f;
        [SerializeField, Min(0f)] private float hideDuration = 0.18f;
        [SerializeField, Range(0.5f, 1f)] private float hiddenScale = 0.9f;
        [Tooltip("Độ vọt của animation mở (OutBack). Nhỏ thôi — chỉ ở khoảnh khắc đến.")]
        [SerializeField, Min(0f)] private float showOvershoot = 1.2f;

        private TaskCompletionSource<bool> _showCompletion;
        private CancellationTokenSource _presentCancellation;
        private float _elapsed;
        private bool _isOpening;
        private bool _isVisible;

        public string DisplayName => displayName;
        public LeaderboardWidget Widget => widget;
        public bool IsVisible => _isVisible;

        public Task<LeaderboardPresentResult> Show(ILeaderboardBoard board, BoardPresentMode mode)
        {
            CancelPresent();
            gameObject.SetActive(true);
            widget.Arm();

            _isVisible = true;
            _isOpening = true;
            _elapsed = 0f;
            _showCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ApplyVisibility(showDuration > 0f ? 0f : 1f);
            if (showDuration <= 0f) _showCompletion.TrySetResult(true);

            _presentCancellation = new CancellationTokenSource();
            return widget.PresentAsync(new LeaderboardPresentRequest(board, mode, _showCompletion.Task), _presentCancellation.Token);
        }

        public void Hide()
        {
            if (!_isVisible || !_isOpening) return;
            widget.Disarm();
            CancelPresent();
            _showCompletion?.TrySetResult(false);
            _isOpening = false;
            _elapsed = 0f;
        }

        private void Update()
        {
            if (!_isVisible) return;
            _elapsed += Time.unscaledDeltaTime;
            if (_isOpening)
            {
                float progress = showDuration > 0f ? Mathf.Clamp01(_elapsed / showDuration) : 1f;
                ApplyVisibility(EaseOutBack(progress, showOvershoot));
                if (progress >= 1f) _showCompletion?.TrySetResult(true);
                return;
            }

            float hideProgress = hideDuration > 0f ? Mathf.Clamp01(_elapsed / hideDuration) : 1f;
            ApplyVisibility(1f - hideProgress);
            if (hideProgress < 1f) return;
            _isVisible = false;
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            CancelPresent();
        }

        private void ApplyVisibility(float amount)
        {
            if (group) group.alpha = Mathf.Clamp01(amount);
            if (panel) panel.localScale = Vector3.one * Mathf.LerpUnclamped(hiddenScale, 1f, amount);
        }

        private void CancelPresent()
        {
            if (_presentCancellation == null) return;
            _presentCancellation.Cancel();
            _presentCancellation.Dispose();
            _presentCancellation = null;
        }

        private static float EaseOutBack(float progress, float overshoot)
        {
            float shifted = progress - 1f;
            return 1f + shifted * shifted * ((overshoot + 1f) * shifted + overshoot);
        }
    }
}
