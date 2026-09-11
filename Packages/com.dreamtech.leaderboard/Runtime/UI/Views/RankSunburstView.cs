using DreamTech.Leaderboard.ViewModel;
using UnityEngine;
using UnityEngine.UI;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>
    /// Tia sáng xoay chậm phía sau row người chơi lúc hạ cánh (chỉ top 3 / #1). Nằm trong Content của list, dưới row mình và
    /// trên các row khác; <see cref="LeaderboardScrollView"/> tự dời theo row mình.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RankSunburstView : MonoBehaviour
    {
        [SerializeField] private Image image;

        private float _elapsed = -1f;
        private float _scale = 1f;
        private float _angle;
        private Color _color = Color.white;
        private LeaderboardVisualSettings _visuals;

        public bool IsPlaying => _elapsed >= 0f;
        public float TimeScale { get; set; } = 1f;
        public RectTransform RectTransform => (RectTransform)transform;

        public void Play(Color color, float scale, LeaderboardVisualSettings visuals)
        {
            _visuals = visuals;
            _elapsed = 0f;
            _color = color;
            _scale = scale;
            gameObject.SetActive(true);
            Apply();
        }

        public void Stop()
        {
            _elapsed = -1f;
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_elapsed < 0f || _visuals == null) return;
            float deltaTime = Time.unscaledDeltaTime * TimeScale;
            _elapsed += deltaTime;
            _angle -= _visuals.SunburstSpinSpeed * deltaTime;
            if (_elapsed >= _visuals.SunburstDuration)
            {
                Stop();
                return;
            }
            Apply();
        }

        private void Apply()
        {
            float duration = Mathf.Max(0.0001f, _visuals.SunburstDuration);
            float progress = _elapsed / duration;
            float appear = Easing.OutCubic(Mathf.Clamp01(_elapsed / 0.45f));
            float fadeIn = Mathf.Clamp01(_elapsed / 0.2f);
            float fadeOut = 1f - Mathf.Clamp01((progress - 0.55f) / 0.45f);
            float scale = _scale * Mathf.LerpUnclamped(0.5f, 1f, appear);
            RectTransform.localScale = new Vector3(scale, scale, 1f);
            RectTransform.localRotation = Quaternion.Euler(0f, 0f, _angle);
            if (image == null) return;
            Color color = _color;
            color.a = _visuals.SunburstMaximumAlpha * fadeIn * fadeOut;
            image.color = color;
        }
    }
}
