using System;
using DreamTech.Leaderboard.ViewModel;
using UnityEngine;
using UnityEngine.UI;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>
    /// "Dòng lên hạng": mũi tên nổi lên trong row người chơi suốt quãng row được nhấc lên và leo, tắt khi row đáp.
    /// Gắn lên prefab row (một con phủ kín row, thường kèm <c>RectMask2D</c> để mũi tên không tràn ra ngoài); không
    /// gắn thì row không vẽ gì — tính năng bật bằng prefab, không bằng cờ.
    ///
    /// <para><b>Thuần hàm của thời gian.</b> Vị trí/độ đục của mũi tên thứ k được TÍNH từ
    /// <see cref="RowState.RankUpStreamStartTime"/>, <see cref="RowState.RankUpStreamEndTime"/> và đồng hồ model —
    /// không giữ trạng thái theo frame. Nhờ vậy view bị thu hồi rồi bind lại giữa chừng vẫn vẽ đúng chỗ, hai view
    /// cùng render một row ra y hệt nhau, và skip (đồng hồ nhảy) không để lại mũi tên mồ côi.</para>
    ///
    /// <para>Số mặc định đo từ clip tham chiếu (24,33 hình/giây): hai mũi bung ngay ở cú lật (đã ở giữa đường bay),
    /// mũi đầu tiên mới sinh sau ~0,12 s, rồi cứ ~0,2 s một mũi; mỗi mũi sống ~0,66 s, đi từ hơi dưới tâm row lên
    /// sát mép trên và hơi tăng tốc; lúc row đáp thì thôi sinh và những mũi còn lại tắt trong ~0,18 s.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class LeaderboardRowRankUpStream : MonoBehaviour
    {
        private const float GoldenRatioFraction = 0.6180339887f;
        private const float PlasticRatioFraction = 0.7548776662f;

        [Tooltip("Image mẫu của một mũi tên (để inactive trong prefab). Được nhân bản thành một pool cố định lần đầu vẽ.")]
        [SerializeField] private Image arrowTemplate;

        [Tooltip("Số mũi tên tối đa cùng lúc. Đủ khi ≥ openingBurst + lifetime / spawnInterval + 1.")]
        [SerializeField, Min(1)] private int poolSize = 8;

        [Header("Nhịp")]
        [Tooltip("Giây giữa hai mũi tên thường.")]
        [SerializeField, Min(0.02f)] private float spawnInterval = 0.2f;

        [Tooltip("Mũi tên thường đầu tiên sinh sau khi dòng bắt đầu bao nhiêu giây.")]
        [SerializeField, Min(0f)] private float firstSpawnDelay = 0.12f;

        [Tooltip("Một mũi tên sống bao nhiêu giây.")]
        [SerializeField, Min(0.05f)] private float lifetime = 0.66f;

        [Tooltip("Số mũi tên bung NGAY lúc dòng bắt đầu, sinh ra đã ở giữa đường bay (clip: hai mũi ở cú lật).")]
        [SerializeField, Min(0)] private int openingBurst = 2;

        [Tooltip("Tuổi (phần của lifetime) của mũi bung đầu tiên lúc dòng bắt đầu.")]
        [SerializeField, Range(0f, 0.95f)] private float openingBurstAge = 0.5f;

        [Tooltip("Mỗi mũi bung kế tiếp trẻ hơn mũi trước bao nhiêu (phần của lifetime) — để chúng không bay thành hàng.")]
        [SerializeField, Range(0f, 0.5f)] private float openingBurstSpread = 0.15f;

        [Tooltip("Sau khi dòng dừng (row đáp), những mũi còn sống vẫn bay nguyên độ sáng thêm bao nhiêu giây rồi mới " +
                 "bắt đầu tắt. 0 (mặc định) = tắt ngay.")]
        [SerializeField, Min(0f)] private float stopFadeDelay;

        [Tooltip("Sau khi dòng dừng (và hết stopFadeDelay), những mũi còn sống tắt hẳn trong bao nhiêu giây.")]
        [SerializeField, Min(0.01f)] private float stopFadeDuration = 0.18f;

        [Header("Đường bay (theo phần kích thước row, gốc ở tâm)")]
        [Tooltip("Độ cao lúc sinh / lúc chết, theo phần chiều cao row tính từ tâm (dương = lên trên).")]
        [SerializeField] private Vector2 riseRange = new Vector2(-0.12f, 0.5f);

        [Tooltip("0 = bay đều, 1 = tăng tốc hẳn (InQuad).")]
        [SerializeField, Range(0f, 1f)] private float riseAcceleration = 0.33f;

        [Tooltip("Dải ngang được phép sinh mũi tên, theo phần bề rộng row (0 = mép trái, 1 = mép phải).")]
        [SerializeField] private Vector2 horizontalRange = new Vector2(0.06f, 0.94f);

        [Tooltip("Dịch chuỗi vị trí ngang (0..1) — đổi để có một bố cục ngẫu nhiên khác mà vẫn cố định.")]
        [SerializeField, Range(0f, 1f)] private float horizontalSeed = 0.1f;

        [Header("Hình")]
        [Tooltip("Bề rộng mũi tên nhỏ nhất / lớn nhất (đơn vị canvas); chiều cao theo tỉ lệ sprite.")]
        [SerializeField] private Vector2 widthRange = new Vector2(40f, 64f);

        [Tooltip("Phần đầu đời để hiện dần (và nở từ appearScale lên 1).")]
        [SerializeField, Range(0f, 0.5f)] private float fadeInFraction = 0.08f;

        [Tooltip("Phần cuối đời để tắt dần.")]
        [SerializeField, Range(0f, 1f)] private float fadeOutFraction = 0.3f;

        [SerializeField, Range(0f, 1f)] private float appearScale = 0.6f;

        private Image[] _arrows;
        private RectTransform[] _arrowTransforms;
        private float[] _appliedAlpha;
        private bool[] _isArrowVisible;
        private RectTransform _container;
        private float _heightPerWidth = 1f;
        private bool _isShowing;

        internal int PoolCount => _arrows != null ? _arrows.Length : 0;

        /// <summary>Số mũi tên đang hiện (để test).</summary>
        internal int VisibleCount
        {
            get
            {
                if (_isArrowVisible == null) return 0;
                int count = 0;
                for (int index = 0; index < _isArrowVisible.Length; index++) if (_isArrowVisible[index]) count++;
                return count;
            }
        }

        /// <summary>Vẽ theo trạng thái của row và đồng hồ model. Gọi mỗi frame từ <see cref="LeaderboardEntryView"/>.</summary>
        public void Render(RowState row, double clock)
        {
            if (row == null || row.IsGap || !row.IsLocalPlayer || !row.HasRankUpStream || arrowTemplate == null)
            {
                Hide();
                return;
            }

            double start = row.RankUpStreamStartTime;
            double end = row.RankUpStreamEndTime;
            bool hasEnded = !double.IsNaN(end);
            float stopFade = hasEnded ? 1f - (float)(Math.Max(0.0, clock - end - stopFadeDelay) / stopFadeDuration) : 1f;
            if (stopFade <= 0f || clock < start)
            {
                Hide();
                return;
            }
            stopFade = Mathf.Clamp01(stopFade);

            EnsurePool();
            Rect area = _container.rect;
            int used = 0;

            for (int burst = 0; burst < openingBurst && used < _arrows.Length; burst++)
            {
                float age = (float)(clock - start) + lifetime * Mathf.Max(0f, openingBurstAge - burst * openingBurstSpread);
                if (DrawArrow(used, burst, age, area, stopFade)) used++;
            }

            double firstBirth = start + firstSpawnDelay;
            if (clock >= firstBirth)
            {
                int newest = (int)Math.Floor((clock - firstBirth) / spawnInterval);
                int oldest = Math.Max(0, (int)Math.Ceiling((clock - lifetime - firstBirth) / spawnInterval));
                for (int index = oldest; index <= newest && used < _arrows.Length; index++)
                {
                    double birth = firstBirth + index * spawnInterval;
                    if (hasEnded && birth > end) break;
                    if (DrawArrow(used, openingBurst + index, (float)(clock - birth), area, stopFade)) used++;
                }
            }

            for (int slot = used; slot < _arrows.Length; slot++) SetArrowVisible(slot, false);
            _isShowing = used > 0;
        }

        public void Hide()
        {
            if (!_isShowing) return;
            _isShowing = false;
            if (_arrows == null) return;
            for (int slot = 0; slot < _arrows.Length; slot++) SetArrowVisible(slot, false);
        }

        private bool DrawArrow(int slot, int sequence, float age, Rect area, float stopFade)
        {
            if (age < 0f || age >= lifetime) return false;
            float progress = age / lifetime;

            float fadeIn = fadeInFraction > 0f ? Mathf.Clamp01(progress / fadeInFraction) : 1f;
            float fadeOut = fadeOutFraction > 0f ? Mathf.Clamp01((1f - progress) / fadeOutFraction) : 1f;
            float alpha = fadeIn * fadeOut * stopFade;
            if (alpha <= 0.002f) return false;

            float horizontal = Mathf.Lerp(horizontalRange.x, horizontalRange.y, Fraction(sequence * GoldenRatioFraction + horizontalSeed));
            float riseProgress = Mathf.Lerp(progress, progress * progress, riseAcceleration);
            float vertical = Mathf.Lerp(riseRange.x, riseRange.y, riseProgress);
            float width = Mathf.Lerp(widthRange.x, widthRange.y, Fraction(sequence * PlasticRatioFraction + 0.37f));
            float scale = Mathf.Lerp(appearScale, 1f, Easing.OutQuad(fadeIn));

            RectTransform arrow = _arrowTransforms[slot];
            // Neo ở tâm container ⇒ anchoredPosition tính từ tâm vùng, đúng hệ toạ độ của riseRange/horizontalRange.
            arrow.anchoredPosition = new Vector2((horizontal - 0.5f) * area.width, vertical * area.height);
            arrow.sizeDelta = new Vector2(width, width * _heightPerWidth);
            arrow.localScale = new Vector3(scale, scale, 1f);

            if (Mathf.Abs(alpha - _appliedAlpha[slot]) > 0.004f)
            {
                _appliedAlpha[slot] = alpha;
                Color color = arrowTemplate.color;
                color.a *= alpha;
                _arrows[slot].color = color;
            }
            SetArrowVisible(slot, true);
            return true;
        }

        private void SetArrowVisible(int slot, bool isVisible)
        {
            if (_isArrowVisible[slot] == isVisible) return;
            _isArrowVisible[slot] = isVisible;
            _arrows[slot].gameObject.SetActive(isVisible);
        }

        private void EnsurePool()
        {
            if (_arrows != null) return;
            arrowTemplate.gameObject.SetActive(false);
            Sprite sprite = arrowTemplate.sprite;
            if (sprite != null && sprite.rect.width > 0f) _heightPerWidth = sprite.rect.height / sprite.rect.width;

            int count = Mathf.Max(1, poolSize);
            _arrows = new Image[count];
            _arrowTransforms = new RectTransform[count];
            _appliedAlpha = new float[count];
            _isArrowVisible = new bool[count];
            Transform parent = arrowTemplate.transform.parent != null ? arrowTemplate.transform.parent : transform;
            _container = (RectTransform)parent;
            for (int slot = 0; slot < count; slot++)
            {
                Image arrow = Instantiate(arrowTemplate, parent, false);
                arrow.name = arrowTemplate.name + " " + slot;
                arrow.raycastTarget = false;
                RectTransform arrowTransform = arrow.rectTransform;
                arrowTransform.anchorMin = arrowTransform.anchorMax = new Vector2(0.5f, 0.5f);
                arrowTransform.pivot = new Vector2(0.5f, 0.5f);
                _arrows[slot] = arrow;
                _arrowTransforms[slot] = arrowTransform;
                _appliedAlpha[slot] = -1f;
            }
        }

        private static float Fraction(float value)
        {
            return value - Mathf.Floor(value);
        }
    }
}
