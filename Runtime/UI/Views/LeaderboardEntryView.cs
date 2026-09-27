using System.Globalization;
using DreamTech.Leaderboard.ViewModel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>
    /// View của một dòng. Chỉ hiển thị: mọi thứ suy ra từ <see cref="RowState"/> + đồng hồ model, nên view nào render cùng
    /// một state ở cùng thời điểm cũng giống hệt nhau — view bị tái sử dụng giữa chừng không mất pill/shine như bản cũ.
    /// Mọi giá trị được cache, chỉ ghi component khi thật sự đổi.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LeaderboardEntryView : MonoBehaviour
    {
        [Header("Khung")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image shadowImage;
        [SerializeField] private Image glowImage;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image flashImage;
        [SerializeField] private GameObject contentRoot;
        [SerializeField] private GameObject gapRoot;
        [SerializeField] private TMP_Text gapText;

        [Header("Hạng")]
        [SerializeField] private Image rankBadgeImage;
        [SerializeField] private TMP_Text rankText;
        [Tooltip("Text thứ hai cho hiệu ứng cuộn số (cùng RectMask2D với rankText).")]
        [SerializeField] private TMP_Text rankRollText;

        [Header("Người chơi")]
        [SerializeField] private Image avatarImage;
        [SerializeField] private TMP_Text avatarInitialText;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text scoreText;
        [Tooltip("Nền sau điểm (tuỳ chọn). Chỉ đổi sprite khi theme có LeaderboardRowSkin.ScoreBackground.")]
        [SerializeField] private Image scoreBackgroundImage;

        [Header("Hiệu ứng")]
        [SerializeField] private RectTransform shineTransform;
        [SerializeField] private CanvasGroup pillGroup;
        [SerializeField] private Image pillBackgroundImage;
        [SerializeField] private Image pillArrowImage;
        [SerializeField] private TMP_Text pillText;

        private bool _isInitialized;
        private ILeaderboardRowDecorator[] _decorators;
        private LeaderboardRowRankUpStream _rankUpStream;
        private RectTransform _rectTransform;
        private RectTransform _pillTransform;
        private Vector2 _pillBasePosition;
        private Vector2 _shadowBasePosition;

        private Vector2 _appliedPosition;
        private float _appliedScale;
        private float _appliedAlpha;
        private float _appliedFlash;
        private float _appliedLift;
        private int _appliedGlowStep;

        private int _shownRank;
        private long _shownScore;
        private string _shownPlayerId;
        private string _shownName;
        private int _shownStyle;
        private int _shownMedal;

        /// <summary>Theme cấp sprite huy hiệu riêng — khi đó không tô màu đè lên art của game.</summary>
        private bool _hasSkinBadge;

        /// <summary>Hạng không có huy chương (row "..." và mọi hạng ngoài top 3).</summary>
        private const int NoMedal = -1;
        private int _shownPillVersion;

        private bool _isPillVisible;
        private float _appliedPillScale;
        private float _appliedPillAlpha;
        private float _appliedPillRise;
        private bool _isShineVisible;
        private bool _isRollActive;
        private int _shownRollPreviousRank;
        private bool _isBadgePunching;
        private DampedSpring _scoreSwell;

        public RowState BoundRow { get; private set; }
        public int BoundContentVersion { get; private set; } = -1;

        public RectTransform RectTransform
        {
            get
            {
                if (_rectTransform == null) _rectTransform = (RectTransform)transform;
                return _rectTransform;
            }
        }

        internal TMP_Text NameText => nameText;
        internal TMP_Text ScoreText => scoreText;
        internal TMP_Text RankText => rankText;
        internal CanvasGroup PillGroup => pillGroup;
        internal TMP_Text PillText => pillText;
        internal RectTransform ShineTransform => shineTransform;
        internal Image GlowImage => glowImage;

        /// <summary>Dựng lại toàn bộ chữ/màu khi đổi row hoặc khi nội dung row đổi.</summary>
        public void Bind(RowState row, LeaderboardRenderContext context)
        {
            EnsureInitialized();
            if (!ReferenceEquals(BoundRow, row)) InvalidateCaches();
            BoundRow = row;
            BoundContentVersion = row.ContentVersion;

            SetActive(gapRoot, row.IsGap);
            SetActive(contentRoot, !row.IsGap);
            if (backgroundImage) backgroundImage.enabled = !row.IsGap;
            if (row.IsGap)
            {
                if (_rankUpStream) _rankUpStream.Hide();
                if (gapText && gapText.text != context.Text.Gap) gapText.text = context.Text.Gap;
                ApplyStyle(false, NoMedal, context.Theme);
                NotifyDecorators(row, context);
                return;
            }

            ApplyStyle(row.IsLocalPlayer, context.TierRule.MedalIndex(row.DisplayRank), context.Theme);
            ApplyIdentity(row, context);
            ApplyRank(row.DisplayRank, context);
            ApplyScore(row.DisplayScore, context.Text.ScoreFormat);
            ApplyPillContent(row, context);
            NotifyDecorators(row, context);
        }

        private void NotifyDecorators(RowState row, LeaderboardRenderContext context)
        {
            for (int index = 0; index < _decorators.Length; index++) _decorators[index].OnRowBound(row, context);
        }

        public void Unbind()
        {
            if (_rankUpStream) _rankUpStream.Hide();
            BoundRow = null;
            BoundContentVersion = -1;
        }

        /// <summary>Đặt vị trí + mọi hiệu ứng theo đồng hồ. Gọi mỗi frame cho các view đang hiện.</summary>
        public void Render(RowState row, Vector2 position, float scale, float alpha, float lift, LeaderboardRenderContext context)
        {
            EnsureInitialized();
            LeaderboardVisualSettings visuals = context.Visuals;
            ApplyLayout(position, scale, alpha, row.Flash, lift, visuals);
            if (row.IsGap) return;

            double clock = context.Clock;
            RenderGlow(row, clock, context);
            RenderRankRoll(row, clock);
            RenderBadgePunch(row, clock, visuals);
            RenderScoreSwell(row, clock, context.DeltaTime, visuals);
            RenderPill(row, clock, context);
            RenderShine(row, clock);
            if (_rankUpStream) _rankUpStream.Render(row, clock);
        }

        // ---------------------------------------------------------------- Khởi tạo

        private void EnsureInitialized()
        {
            if (_isInitialized) return;
            _isInitialized = true;
            _decorators = GetComponentsInChildren<ILeaderboardRowDecorator>(true);
            _rankUpStream = GetComponentInChildren<LeaderboardRowRankUpStream>(true);
            if (_rankUpStream) _rankUpStream.Hide();

            if (pillGroup)
            {
                _pillTransform = (RectTransform)pillGroup.transform;
                _pillBasePosition = _pillTransform.anchoredPosition;
                pillGroup.gameObject.SetActive(false);
            }
            if (shadowImage)
            {
                _shadowBasePosition = shadowImage.rectTransform.anchoredPosition;
                shadowImage.enabled = false;
            }
            if (shineTransform) shineTransform.gameObject.SetActive(false);
            if (flashImage) flashImage.enabled = false;
            if (rankRollText) rankRollText.text = string.Empty;
            if (nameText)
            {
                // Tên do backend trả về: không cho chèn thẻ rich text, và luôn Ellipsis kể cả khi TMP lỡ tự đổi sang Truncate.
                nameText.richText = false;
                nameText.overflowMode = TextOverflowModes.Ellipsis;
            }
            _scoreSwell.Reset(1f);
            InvalidateCaches();
        }

        private void InvalidateCaches()
        {
            _appliedPosition = new Vector2(float.NaN, float.NaN);
            _appliedScale = -1f;
            _appliedAlpha = -1f;
            _appliedFlash = -1f;
            _appliedLift = -1f;
            _appliedGlowStep = -1;
            _shownRank = int.MinValue;
            _shownScore = long.MinValue;
            _shownPlayerId = null;
            _shownName = null;
            _shownStyle = -1;
            _shownMedal = int.MinValue;
            _shownPillVersion = -1;
            _appliedPillScale = -1f;
            _appliedPillAlpha = -1f;
            _appliedPillRise = -1f;
            _shownRollPreviousRank = int.MinValue;
            if (_isRollActive) EndRoll();
            if (_isBadgePunching && rankBadgeImage) rankBadgeImage.rectTransform.localScale = Vector3.one;
            _isBadgePunching = false;
            _scoreSwell.Reset(1f);
            if (scoreText) scoreText.rectTransform.localScale = Vector3.one;
        }

        // ---------------------------------------------------------------- Nội dung tĩnh

        /// <summary>
        /// Bộ mặt của row: nền, màu chữ, glow. Phụ thuộc cả "có phải mình không" lẫn hạng đang hiện, vì leo lên top 3 thì row
        /// phải đổi mặt ngay giữa animation. Khoá cache gộp cả hai để mỗi lần đổi thật mới ghi xuống component.
        ///
        /// <para>Theme có khai báo <see cref="LeaderboardRowSkin"/> thì dùng art của game (9-slice + huy hiệu rời);
        /// không thì giữ đường cũ — một sprite grayscale tô màu theo theme.</para>
        /// </summary>
        private void ApplyStyle(bool isLocalPlayer, int medalIndex, LeaderboardThemeConfig theme)
        {
            int style = (medalIndex + 1) * 2 + (isLocalPlayer ? 1 : 0);
            if (style == _shownStyle) return;
            _shownStyle = style;

            LeaderboardRowSkin skin = theme.HasRowSkins ? theme.RowSkin(medalIndex, isLocalPlayer) : null;
            if (skin != null)
            {
                if (backgroundImage && skin.Background)
                {
                    backgroundImage.sprite = skin.Background;
                    backgroundImage.color = Color.white;
                }
                if (nameText) nameText.color = skin.NameColor;
                if (scoreText) scoreText.color = skin.ScoreColor;
                if (scoreBackgroundImage && skin.ScoreBackground) scoreBackgroundImage.sprite = skin.ScoreBackground;
                _hasSkinBadge = skin.Badge != null;
                if (rankBadgeImage && _hasSkinBadge)
                {
                    rankBadgeImage.sprite = skin.Badge;
                    rankBadgeImage.color = Color.white;
                }
                if (rankText) SetActive(rankText.gameObject, skin.ShowRankNumber);
            }
            else
            {
                _hasSkinBadge = false;
                if (backgroundImage) backgroundImage.color = isLocalPlayer ? theme.LocalRowBackgroundColor : theme.RowBackgroundColor;
                if (nameText) nameText.color = isLocalPlayer ? theme.LocalNameColor : theme.NameColor;
                if (scoreText) scoreText.color = isLocalPlayer ? theme.LocalScoreColor : theme.ScoreColor;
            }

            if (glowImage)
            {
                glowImage.gameObject.SetActive(isLocalPlayer);
                _appliedGlowStep = -1;
            }
        }

        private void ApplyIdentity(RowState row, LeaderboardRenderContext context)
        {
            LeaderboardEntry entry = row.Entry;
            string displayName = ResolveDisplayName(row, context.Text);
            if (_shownPlayerId == entry.PlayerId && _shownName == displayName) return;
            _shownPlayerId = entry.PlayerId;
            _shownName = displayName;

            if (nameText) nameText.text = displayName;
            if (avatarImage)
            {
                float hue = (StableHash(entry.PlayerId) & 0xFFFF) / 65535f;
                avatarImage.color = Color.HSVToRGB(hue, context.Theme.AvatarSaturation, context.Theme.AvatarValue);
            }
            if (avatarInitialText) avatarInitialText.text = FirstLetter(displayName);
        }

        private void ApplyRank(int rank, LeaderboardRenderContext context)
        {
            if (rank == _shownRank) return;
            _shownRank = rank;
            if (rankText) rankText.text = (rank + 1).ToString(CultureInfo.InvariantCulture);

            int medal = context.TierRule.MedalIndex(rank);
            if (medal == _shownMedal) return;
            _shownMedal = medal;
            if (rankBadgeImage && !_hasSkinBadge) rankBadgeImage.color = context.Theme.MedalColor(medal);
        }

        private void ApplyScore(long score, string format)
        {
            if (score == _shownScore) return;
            _shownScore = score;
            if (scoreText) scoreText.text = score.ToString(format, CultureInfo.InvariantCulture);
        }

        private void ApplyPillContent(RowState row, LeaderboardRenderContext context)
        {
            if (!row.PillTiming.IsStarted) return;
            // So theo chính nội dung pill (không theo ContentVersion, vốn tăng mỗi frame lúc đếm điểm) để không tạo chuỗi thừa.
            int pillKey = ((int)row.PillContent * 397) ^ row.PillValue ^ row.PillTiming.StartTime.GetHashCode();
            if (pillKey == _shownPillVersion) return;
            _shownPillVersion = pillKey;
            if (pillText)
            {
                string label = row.PillContent == PillContent.RankUp ? row.PillValue.ToString(CultureInfo.InvariantCulture)
                    : row.PillContent == PillContent.New ? context.Text.NewPill
                    : context.Text.BestPill;
                if (pillText.text != label) pillText.text = label;
            }
            if (pillArrowImage) SetActive(pillArrowImage.gameObject, row.PillContent == PillContent.RankUp);
            if (pillBackgroundImage) pillBackgroundImage.color = context.Theme.PillColor;
        }

        // ---------------------------------------------------------------- Hiệu ứng theo đồng hồ

        private void ApplyLayout(Vector2 position, float scale, float alpha, float flashAlpha, float lift, LeaderboardVisualSettings visuals)
        {
            RectTransform rectTransform = RectTransform;
            if (float.IsNaN(_appliedPosition.x) || (position - _appliedPosition).sqrMagnitude > 0.0001f)
            {
                _appliedPosition = position;
                rectTransform.anchoredPosition = position;
            }
            if (Mathf.Abs(scale - _appliedScale) > 0.00005f)
            {
                _appliedScale = scale;
                rectTransform.localScale = new Vector3(scale, scale, 1f);
            }
            if (canvasGroup && Mathf.Abs(alpha - _appliedAlpha) > 0.001f)
            {
                _appliedAlpha = alpha;
                canvasGroup.alpha = alpha;
            }
            if (flashImage && Mathf.Abs(flashAlpha - _appliedFlash) > 0.002f)
            {
                _appliedFlash = flashAlpha;
                flashImage.enabled = flashAlpha > 0.002f;
                Color color = flashImage.color;
                color.a = flashAlpha;
                flashImage.color = color;
            }
            if (shadowImage && Mathf.Abs(lift - _appliedLift) > 0.002f)
            {
                _appliedLift = lift;
                shadowImage.enabled = lift > 0.002f;
                Color color = shadowImage.color;
                color.a = visuals.ShadowMaximumAlpha * lift;
                shadowImage.color = color;
                shadowImage.rectTransform.anchoredPosition = _shadowBasePosition + new Vector2(0f, -visuals.ShadowOffset * lift);
            }
        }

        private void RenderGlow(RowState row, double clock, LeaderboardRenderContext context)
        {
            if (!row.IsLocalPlayer || glowImage == null) return;
            LeaderboardVisualSettings visuals = context.Visuals;
            float pulse = 0.5f + 0.5f * Mathf.Sin((float)(clock * visuals.GlowPulseSpeed));
            float flashGlow = visuals.GlowFlashAlpha > 0f
                ? visuals.GlowFlashAlpha * Mathf.Pow(row.FlashLevel, Mathf.Max(0.01f, visuals.GlowFlashPower))
                : 0f;
            float alpha = Mathf.Clamp01(visuals.GlowBaseAlpha + visuals.GlowPulseAlpha * pulse + row.GlowBoost * visuals.GlowBoostAlpha +
                                        flashGlow);
            int steps = Mathf.Max(1, visuals.GlowAlphaSteps);
            int step = Mathf.RoundToInt(alpha * steps);
            if (step == _appliedGlowStep) return;
            _appliedGlowStep = step;
            Color color = context.Theme.GlowColor;
            color.a = (float)step / steps;
            glowImage.color = color;
        }

        private void RenderRankRoll(RowState row, double clock)
        {
            if (rankText == null || rankRollText == null) return;
            TimedEffect timing = row.RankRollTiming;
            if (timing.IsRunning(clock))
            {
                if (!_isRollActive || _shownRollPreviousRank != row.RankRollPreviousRank)
                {
                    _isRollActive = true;
                    _shownRollPreviousRank = row.RankRollPreviousRank;
                    rankRollText.text = (row.RankRollPreviousRank + 1).ToString(CultureInfo.InvariantCulture);
                }
                float height = rankText.rectTransform.rect.height;
                float eased = Easing.OutCubic(timing.Progress(clock));
                float direction = row.RankRollDirection;
                rankText.rectTransform.anchoredPosition = new Vector2(0f, Mathf.LerpUnclamped(direction * height, 0f, eased));
                rankRollText.rectTransform.anchoredPosition = new Vector2(0f, Mathf.LerpUnclamped(0f, -direction * height, eased));
            }
            else if (_isRollActive)
            {
                EndRoll();
            }
        }

        private void EndRoll()
        {
            _isRollActive = false;
            _shownRollPreviousRank = int.MinValue;
            if (rankText) rankText.rectTransform.anchoredPosition = Vector2.zero;
            if (rankRollText) rankRollText.text = string.Empty;
        }

        private void RenderBadgePunch(RowState row, double clock, LeaderboardVisualSettings visuals)
        {
            if (rankBadgeImage == null) return;
            TimedEffect timing = row.BadgePunchTiming;
            if (timing.IsRunning(clock))
            {
                _isBadgePunching = true;
                float scale = Mathf.LerpUnclamped(visuals.BadgePunchStartScale, 1f, Easing.OutBack(timing.Progress(clock), visuals.BadgePunchOvershoot));
                rankBadgeImage.rectTransform.localScale = new Vector3(scale, scale, 1f);
            }
            else if (_isBadgePunching)
            {
                _isBadgePunching = false;
                rankBadgeImage.rectTransform.localScale = Vector3.one;
            }
        }

        private void RenderScoreSwell(RowState row, double clock, float deltaTime, LeaderboardVisualSettings visuals)
        {
            if (scoreText == null) return;
            bool isCounting = clock - row.LastScoreChangeTime < visuals.ScoreSwellWindow;
            _scoreSwell.Target = isCounting ? visuals.ScoreSwellScale : 1f;
            if (!isCounting && _scoreSwell.IsSettled) return;
            _scoreSwell.Step(deltaTime, visuals.ScoreSwellStiffness, visuals.ScoreSwellDamping);
            float value = _scoreSwell.Value;
            scoreText.rectTransform.localScale = new Vector3(value, value, 1f);
        }

        private void RenderPill(RowState row, double clock, LeaderboardRenderContext context)
        {
            if (pillGroup == null) return;
            TimedEffect timing = row.PillTiming;
            if (!timing.IsRunning(clock))
            {
                if (!_isPillVisible) return;
                _isPillVisible = false;
                _pillTransform.anchoredPosition = _pillBasePosition;
                _pillTransform.localScale = Vector3.one;
                pillGroup.gameObject.SetActive(false);
                return;
            }

            if (!_isPillVisible)
            {
                _isPillVisible = true;
                _shownPillVersion = -1;
                pillGroup.gameObject.SetActive(true);
            }
            ApplyPillContent(row, context);

            MotionSettings motion = context.Motion;
            LeaderboardVisualSettings visuals = context.Visuals;
            float elapsed = timing.Elapsed(clock);
            float scale = 1f;
            float alpha = 1f;
            float rise = 0f;
            if (elapsed < motion.PillPopDuration)
            {
                float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, motion.PillPopDuration));
                scale = Easing.OutBack(progress, visuals.PillPopOvershoot);
                alpha = Mathf.Clamp01(progress * 3f);
            }
            else if (elapsed >= motion.PillPopDuration + motion.PillHoldDuration)
            {
                float progress = Mathf.Clamp01((elapsed - motion.PillPopDuration - motion.PillHoldDuration) / Mathf.Max(0.0001f, motion.PillRiseDuration));
                rise = visuals.PillRiseDistance * Easing.OutCubic(progress);
                alpha = 1f - progress;
            }

            // Vị trí luôn = gốc cố định của prefab + độ nâng, nên bị ngắt giữa chừng cũng không trôi dần như bản cũ.
            if (Mathf.Abs(scale - _appliedPillScale) > 0.0005f)
            {
                _appliedPillScale = scale;
                _pillTransform.localScale = new Vector3(scale, scale, 1f);
            }
            if (Mathf.Abs(alpha - _appliedPillAlpha) > 0.002f)
            {
                _appliedPillAlpha = alpha;
                pillGroup.alpha = alpha;
            }
            if (Mathf.Abs(rise - _appliedPillRise) > 0.01f)
            {
                _appliedPillRise = rise;
                _pillTransform.anchoredPosition = _pillBasePosition + new Vector2(0f, rise);
            }
        }

        private void RenderShine(RowState row, double clock)
        {
            if (shineTransform == null) return;
            TimedEffect timing = row.ShineTiming;
            if (!timing.IsRunning(clock))
            {
                if (!_isShineVisible) return;
                _isShineVisible = false;
                shineTransform.gameObject.SetActive(false);
                return;
            }
            if (!_isShineVisible)
            {
                _isShineVisible = true;
                shineTransform.gameObject.SetActive(true);
            }
            float halfTravel = RectTransform.rect.width * 0.5f + shineTransform.rect.width;
            float eased = Easing.InOutCubic(timing.Progress(clock));
            shineTransform.anchoredPosition = new Vector2(Mathf.LerpUnclamped(-halfTravel, halfTravel, eased), 0f);
        }

        // ---------------------------------------------------------------- Tiện ích

        private static string ResolveDisplayName(RowState row, LeaderboardTextConfig text)
        {
            if (row.IsLocalPlayer && !string.IsNullOrEmpty(text.LocalPlayerName)) return text.LocalPlayerName;
            string name = row.Entry.DisplayName;
            if (string.IsNullOrWhiteSpace(name)) return text.FallbackPlayerName;
            // Tên nhiều dòng làm vỡ row: gộp về một dòng.
            return name.IndexOfAny(LineBreakCharacters) >= 0 ? name.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ') : name;
        }

        private static readonly char[] LineBreakCharacters = { '\n', '\r', '\t' };

        /// <summary>
        /// Chữ trên avatar placeholder: chữ cái hoặc chữ số đầu tiên của tên, để tên kiểu "_x" hay "&lt;color=red&gt;..." không
        /// hiện ký tự rác. Tên không có chữ/số nào thì lấy ký tự hiển thị đầu tiên. Duyệt theo text element nên chữ có dấu
        /// tổ hợp và emoji không bị cắt đôi.
        /// </summary>
        internal static string FirstLetter(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            string firstVisibleElement = null;
            TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(name);
            while (elements.MoveNext())
            {
                string element = elements.GetTextElement();
                if (char.IsLetterOrDigit(element, 0)) return element.ToUpperInvariant();
                if (firstVisibleElement == null && !string.IsNullOrWhiteSpace(element)) firstVisibleElement = element;
            }
            return firstVisibleElement != null ? firstVisibleElement.ToUpperInvariant() : "?";
        }

        // FNV-1a: ổn định giữa các lần chạy và nền tảng.
        private static uint StableHash(string value)
        {
            uint hash = 2166136261;
            if (value == null) return hash;
            for (int index = 0; index < value.Length; index++)
            {
                hash ^= value[index];
                hash *= 16777619;
            }
            return hash;
        }

        private static void SetActive(GameObject target, bool isActive)
        {
            if (target != null && target.activeSelf != isActive) target.SetActive(isActive);
        }
    }
}
