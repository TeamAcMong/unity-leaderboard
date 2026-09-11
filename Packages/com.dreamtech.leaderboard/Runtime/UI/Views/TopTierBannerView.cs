using DreamTech.Leaderboard.ViewModel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>
    /// Banner cho khoảnh khắc lớn (top 3 / #1): nở ra êm, giữ một nhịp, rồi mờ đi. Nằm ở lớp overlay NGOÀI mask của list;
    /// widget đặt tâm dọc mỗi frame để banner luôn ở dưới (hoặc trên) row người chơi, không bao giờ che row.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TopTierBannerView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private RectTransform body;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text subtitleText;

        private enum Phase
        {
            Hidden,
            In,
            Hold,
            Out,
        }

        private Phase _phase = Phase.Hidden;
        private float _elapsed;
        private float _baseY;
        private LeaderboardVisualSettings _visuals;

        public bool IsVisible => _phase != Phase.Hidden;
        public float TimeScale { get; set; } = 1f;
        public float BodyHeight => body ? body.rect.height : 0f;
        public RectTransform Body => body;

        public void Show(string title, string subtitle, Color accent, LeaderboardVisualSettings visuals)
        {
            _visuals = visuals;
            gameObject.SetActive(true);
            if (titleText) titleText.text = title;
            if (subtitleText)
            {
                subtitleText.text = subtitle ?? string.Empty;
                subtitleText.gameObject.SetActive(!string.IsNullOrEmpty(subtitle));
            }
            if (backgroundImage) backgroundImage.color = accent;
            _phase = Phase.In;
            _elapsed = 0f;
            Apply();
        }

        /// <summary>Đặt anchoredPosition.y gốc của body (widget đã quy đổi từ tâm mong muốn); pha mờ đi cộng thêm độ nâng.</summary>
        public void SetCenterY(float anchoredY)
        {
            _baseY = anchoredY;
            if (IsVisible) Apply();
        }

        public void Hide()
        {
            _phase = Phase.Hidden;
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_phase == Phase.Hidden || _visuals == null) return;
            _elapsed += Time.unscaledDeltaTime * TimeScale;
            switch (_phase)
            {
                case Phase.In when _elapsed >= _visuals.BannerInDuration:
                    _elapsed -= _visuals.BannerInDuration;
                    _phase = Phase.Hold;
                    break;
                case Phase.Hold when _elapsed >= _visuals.BannerHoldDuration:
                    _elapsed -= _visuals.BannerHoldDuration;
                    _phase = Phase.Out;
                    break;
                case Phase.Out when _elapsed >= _visuals.BannerOutDuration:
                    Hide();
                    return;
            }
            Apply();
        }

        private void Apply()
        {
            if (_visuals == null) return;
            float scale = 1f;
            float alpha = 1f;
            float rise = 0f;
            float titleScale = 1f;
            switch (_phase)
            {
                case Phase.In:
                {
                    float duration = Mathf.Max(0.0001f, _visuals.BannerInDuration);
                    float progress = Mathf.Clamp01(_elapsed / duration);
                    scale = Mathf.LerpUnclamped(0.6f, 1f, Easing.OutBack(progress, 1.5f));
                    alpha = Mathf.Clamp01(progress * 3f);
                    // Chữ tiêu đề nảy trễ một nhịp so với nền: cảm giác có lớp.
                    float titleProgress = Mathf.Clamp01((_elapsed - 0.08f) / duration);
                    titleScale = Mathf.LerpUnclamped(0.8f, 1f, Easing.OutBack(titleProgress, 1.8f));
                    break;
                }
                case Phase.Out:
                {
                    float progress = Mathf.Clamp01(_elapsed / Mathf.Max(0.0001f, _visuals.BannerOutDuration));
                    float eased = Easing.OutCubic(progress);
                    scale = Mathf.Lerp(1f, 0.96f, eased);
                    alpha = 1f - progress;
                    rise = _visuals.BannerOutRise * eased;
                    break;
                }
            }
            if (group) group.alpha = alpha;
            if (titleText) titleText.rectTransform.localScale = new Vector3(titleScale, titleScale, 1f);
            if (!body) return;
            body.localScale = new Vector3(scale, scale, 1f);
            body.anchoredPosition = new Vector2(body.anchoredPosition.x, _baseY + rise);
        }
    }
}
