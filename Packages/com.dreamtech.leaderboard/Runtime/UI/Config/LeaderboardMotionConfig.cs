using System;
using DreamTech.Leaderboard.ViewModel;
using UnityEngine;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>Tham số chỉ thuộc về phần hình ảnh (độ lớn, alpha, vật lý hạt) — view-model không cần biết.</summary>
    [Serializable]
    public sealed class LeaderboardVisualSettings
    {
        [Header("Row")]
        [Tooltip("Alpha tối đa của bóng đổ khi row được nhấc hẳn lên.")]
        public float ShadowMaximumAlpha = 0.45f;
        [Tooltip("Bóng đổ dịch xuống bao nhiêu px khi nhấc hẳn lên.")]
        public float ShadowOffset = 18f;
        public float GlowBaseAlpha = 0.25f;
        public float GlowPulseAlpha = 0.2f;
        public float GlowPulseSpeed = 3f;
        public float GlowBoostAlpha = 0.35f;
        [Tooltip("Glow sáng thêm theo cú loé của row: alpha glow += hệ số này × FlashLevel^GlowFlashPower (FlashLevel " +
                 "= độ loé so với đỉnh, 0..1). 0 (mặc định) = glow không đi theo flash, như cũ. Clip tham chiếu: viền " +
                 "sáng bừng lên cùng cú loé lúc đáp rồi tắt cùng nó.")]
        public float GlowFlashAlpha;
        [Tooltip("Hình của phần glow đi theo flash. 1 = tắt cùng nhịp flash; lớn hơn = viền tắt SỚM hơn lớp loé trên " +
                 "row (clip: viền đã tắt quá nửa khi row mới tắt một phần tư).")]
        public float GlowFlashPower = 1f;
        [Tooltip("Số bậc alpha của glow: chỉ ghi màu khi đổi bậc, tránh rebuild canvas mỗi frame.")]
        public int GlowAlphaSteps = 64;
        public float PillPopOvershoot = 1.8f;
        public float PillRiseDistance = 22f;
        public float BadgePunchStartScale = 1.3f;
        public float BadgePunchOvershoot = 2.2f;
        public float ScoreSwellScale = 1.06f;
        [Tooltip("Điểm vừa đổi trong khoảng này thì coi như đang đếm (phồng nhẹ).")]
        public float ScoreSwellWindow = 0.09f;
        public float ScoreSwellStiffness = 300f;
        public float ScoreSwellDamping = 22f;

        [Header("Ăn mừng (chỉ top 3 / #1)")]
        public float SunburstDuration = 1.6f;
        public float SunburstSpinSpeed = 18f;
        [Range(0f, 1f)] public float SunburstMaximumAlpha = 0.4f;
        public float SunburstFirstPlaceScale = 1.2f;
        public float SunburstPodiumScale = 1f;
        public float BannerInDuration = 0.4f;
        public float BannerHoldDuration = 1.1f;
        public float BannerOutDuration = 0.25f;
        public float BannerOutRise = 30f;
        [Tooltip("Khoảng cách banner với row người chơi.")]
        public float BannerGap = 12f;
        public int TwinkleCount = 9;
        [Tooltip("Sao cộng thêm cho mỗi bậc tier trên Standard.")]
        public int TwinkleBonusPerTier = 3;
        public int ConfettiFirstPlace = 110;
        public int ConfettiPodium = 70;

        [Header("Hạt")]
        public Vector2 ConfettiSpeedRange = new Vector2(850f, 1500f);
        public float ConfettiSpreadAngle = 60f;
        public float ConfettiGravity = 2300f;
        public float ConfettiDrag = 1.8f;
        public Vector2 ConfettiLifetimeRange = new Vector2(1.1f, 1.6f);
        public Vector2 ConfettiSizeMinimum = new Vector2(14f, 8f);
        public Vector2 ConfettiSizeMaximum = new Vector2(24f, 13f);
        public Vector2 TwinkleLifetimeRange = new Vector2(0.55f, 0.95f);
        public Vector2 TwinkleSizeRange = new Vector2(26f, 46f);
        [Tooltip("Mỗi ngôi sao nở trễ ngẫu nhiên trong [0, giá trị này] giây — để chúng không bung cùng một nhịp. " +
                 "0,25 (mặc định) như cũ.")]
        public float TwinkleMaximumDelay = 0.25f;
        [Tooltip("Phần sao rải TRÊN MẶT row (đều trong khung row) thay vì trên viền ngoài. 0 (mặc định) = chỉ viền, như " +
                 "cũ. Clip tham chiếu: sao nở cả trên mặt hàng lẫn quanh viền.")]
        [Range(0f, 1f)] public float TwinkleInsideFraction;

        [Header("Trạng thái")]
        [Tooltip("Chỉ hiện chữ Loading nếu tải lâu hơn khoảng này, để lần tải nhanh không bị nháy.")]
        public float LoadingIndicatorDelay = 0.25f;
        public float LoadingDotsPerSecond = 3f;
        [Tooltip("Chờ host (popup) diễn xong animation mở tối đa bao lâu trước khi bắt đầu intro.")]
        public float HostReadyTimeout = 1.5f;
        [Tooltip("Dựng list NGAY khi dữ liệu về, trước khi host sẵn sàng: model, row của người chơi (ô TRƯỚC màn diễn) sát mép " +
                 "trên khung nhìn — đỉnh list khi row đó nằm trong phần host trình bày — và mọi row ở tư thế đầu của đợt trượt " +
                 "(chưa hiện). Host sẵn sàng thì list canh lại và đợt trượt bắt đầu như cũ. Widget bắn ListStaged để host đổ " +
                 "phần nó tự vẽ (bục...) từ khung đầu. Tắt (mặc định) = nội dung trống tới lúc host sẵn sàng, như cũ.")]
        public bool StageListBeforeHostReady;
    }

    /// <summary>
    /// Toàn bộ nhịp chuyển động của leaderboard ở một chỗ, designer chỉnh trong inspector. Mặc định = "premium smooth".
    /// </summary>
    [CreateAssetMenu(menuName = "DreamTech/Leaderboard/Motion Config", fileName = "LeaderboardMotionConfig")]
    public sealed class LeaderboardMotionConfig : ScriptableObject
    {
        [SerializeField] private MotionSettings timeline = new MotionSettings();
        [SerializeField] private LeaderboardVisualSettings visuals = new LeaderboardVisualSettings();

        [Header("Đường cong vẽ tay (0.4.0) — để trống = giữ easing cũ")]
        [Tooltip("Độ lùi của cú trượt vào, 0 → 1 theo tiến độ. Thay OutCubic / OutBack(IntroSlideOvershoot).")]
        [SerializeField] private AnimationCurve introSlideCurve = new AnimationCurve();
        [Tooltip("Cú nhấc: cỡ 1 → LiftScale theo curve(p). Thay OutCubic.")]
        [SerializeField] private AnimationCurve liftCurve = new AnimationCurve();
        [Tooltip("Tiến độ cú leo (ô xuất phát → ô đích). Thay InOut theo ClimbEasePower.")]
        [SerializeField] private AnimationCurve climbCurve = new AnimationCurve();
        [Tooltip("Cú đáp: cỡ = lerp(cỡ lúc đáp, 1, curve(p)); giá trị ngoài [0,1] là vọt/hụt. Thay OutBack và cú đáp ba đoạn.")]
        [SerializeField] private AnimationCurve landCurve = new AnimationCurve();

        public LeaderboardVisualSettings Visuals => visuals ?? (visuals = new LeaderboardVisualSettings());

        /// <summary>Bản sao để chụp lúc bắt đầu diễn (chỉnh config giữa chừng không làm lệch màn đang chạy).</summary>
        public MotionSettings CreateSettings()
        {
            MotionSettings settings = (timeline ?? new MotionSettings()).Clone();
            settings.IntroSlideCurve = ToKeyframeCurve(introSlideCurve);
            settings.LiftCurve = ToKeyframeCurve(liftCurve);
            settings.ClimbCurve = ToKeyframeCurve(climbCurve);
            settings.LandCurve = ToKeyframeCurve(landCurve);
            return settings;
        }

        /// <summary>
        /// <c>AnimationCurve</c> → <see cref="KeyframeCurve"/> (view-model không có Unity). Không khoá = null = tắt. Chép đủ
        /// tiếp tuyến lẫn trọng số: KeyframeCurve đánh giá y hệt AnimationCurve.
        /// </summary>
        public static KeyframeCurve ToKeyframeCurve(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0) return null;
            var keys = new KeyframeCurve.Key[curve.length];
            for (int index = 0; index < curve.length; index++)
            {
                Keyframe key = curve[index];
                keys[index] = new KeyframeCurve.Key(key.time, key.value, key.inTangent, key.outTangent,
                                                    (KeyframeCurve.WeightedMode)(int)key.weightedMode, key.inWeight, key.outWeight);
            }
            return new KeyframeCurve(keys);
        }
    }
}
