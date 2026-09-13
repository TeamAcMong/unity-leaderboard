using System;
using UnityEngine;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>
    /// Bộ mặt của một kiểu row khi game có art riêng cho từng hạng (hạng 1 vàng, hạng 2 bạc, row của mình nổi bật...).
    /// Để trống <see cref="Background"/> thì row giữ cách cũ: một sprite duy nhất tô màu theo theme.
    ///
    /// <para>Sprite nền nên là 9-slice — row co giãn theo bề rộng list, cắt ảnh nguyên tấm sẽ méo góc bo.</para>
    /// </summary>
    [Serializable]
    public sealed class LeaderboardRowSkin
    {
        [Tooltip("Nền row, nên là 9-slice. Để trống = skin này không được dùng (row rơi về skin khác hoặc cách tô màu cũ).")]
        public Sprite Background;

        [Tooltip("Huy hiệu hạng (hạng 1–3). Để trống thì badge giữ sprite sẵn có và chỉ đổi màu.")]
        public Sprite Badge;

        [Tooltip("Tắt khi huy hiệu đã vẽ sẵn số hạng.")]
        public bool ShowRankNumber = true;

        [Tooltip("Nền sau điểm (pill), nên là 9-slice. Để trống thì giữ sprite sẵn có trên prefab.")]
        public Sprite ScoreBackground;

        public Color NameColor = Color.white;
        public Color ScoreColor = Color.white;
    }

    /// <summary>Màu sắc của leaderboard. Sprite mặc định là grayscale nên chỉ cần đổi màu ở đây để re-skin.</summary>
    [CreateAssetMenu(menuName = "DreamTech/Leaderboard/Theme Config", fileName = "LeaderboardThemeConfig")]
    public sealed class LeaderboardThemeConfig : ScriptableObject
    {
        [Header("Row")]
        [SerializeField] private Color rowBackgroundColor = new Color32(0x5E, 0x95, 0xF2, 0xFF);
        [SerializeField] private Color localRowBackgroundColor = new Color32(0xFF, 0xB5, 0x2E, 0xFF);
        [SerializeField] private Color nameColor = Color.white;
        [SerializeField] private Color localNameColor = Color.white;
        [SerializeField] private Color scoreColor = new Color32(0xFF, 0xF1, 0xB0, 0xFF);
        [SerializeField] private Color localScoreColor = Color.white;
        [SerializeField] private Color glowColor = new Color(1f, 0.84f, 0.3f, 1f);

        [Header("Badge hạng")]
        [SerializeField] private Color badgeIdleColor = new Color(0f, 0f, 0f, 0.22f);
        [Tooltip("Màu huy chương theo hạng: 0 = hạng nhất, 1 = nhì, 2 = ba.")]
        [SerializeField] private Color[] medalColors =
        {
            new Color32(0xFF, 0xC9, 0x2E, 0xFF),
            new Color32(0xC6, 0xD4, 0xE6, 0xFF),
            new Color32(0xE0, 0x8A, 0x4E, 0xFF),
        };

        [Header("Art theo hạng (để trống = chỉ tô màu như cũ)")]
        [Tooltip("Row của hạng 1, 2, 3 — theo thứ tự. Thiếu phần tử nào thì hạng đó rơi về row thường.")]
        [SerializeField] private LeaderboardRowSkin[] medalRowSkins = new LeaderboardRowSkin[0];
        [SerializeField] private LeaderboardRowSkin defaultRowSkin;
        [Tooltip("Row của chính người chơi. Ưu tiên cao hơn huy chương: đứng nhất thì vẫn là row 'mình'.")]
        [SerializeField] private LeaderboardRowSkin localRowSkin;

        [Header("Tier (banner, tia sáng)")]
        [SerializeField] private Color firstPlaceColor = new Color32(0xFF, 0xC4, 0x2E, 0xFF);
        [SerializeField] private Color podiumColor = new Color32(0xFF, 0x8F, 0x3D, 0xFF);
        [SerializeField] private Color pillColor = new Color32(0x3C, 0xC2, 0x5E, 0xFF);

        [Header("Avatar placeholder (màu theo PlayerId)")]
        [SerializeField, Range(0f, 1f)] private float avatarSaturation = 0.5f;
        [SerializeField, Range(0f, 1f)] private float avatarValue = 0.92f;

        [Header("Hạt")]
        [SerializeField] private Color twinkleColor = new Color32(0xFF, 0xF6, 0xD0, 0xFF);
        [SerializeField] private Color[] confettiPalette =
        {
            new Color32(0xFF, 0xD5, 0x4A, 0xFF), new Color32(0xFF, 0x6B, 0x7A, 0xFF), new Color32(0x5C, 0xD6, 0xFF, 0xFF),
            new Color32(0x86, 0xF0, 0x8E, 0xFF), new Color32(0xC9, 0x8C, 0xFF, 0xFF), Color.white,
        };

        public Color RowBackgroundColor => rowBackgroundColor;
        public Color LocalRowBackgroundColor => localRowBackgroundColor;
        public Color NameColor => nameColor;
        public Color LocalNameColor => localNameColor;
        public Color ScoreColor => scoreColor;
        public Color LocalScoreColor => localScoreColor;
        public Color GlowColor => glowColor;
        public Color BadgeIdleColor => badgeIdleColor;
        public Color PillColor => pillColor;
        public float AvatarSaturation => avatarSaturation;
        public float AvatarValue => avatarValue;
        public Color TwinkleColor => twinkleColor;
        public Color[] ConfettiPalette => confettiPalette;

        /// <summary>True khi theme có art riêng cho row; false thì row chạy đường tô màu như trước.</summary>
        public bool HasRowSkins
        {
            get
            {
                if (IsDeclared(localRowSkin) || IsDeclared(defaultRowSkin)) return true;
                if (medalRowSkins == null) return false;
                for (int index = 0; index < medalRowSkins.Length; index++)
                {
                    if (IsDeclared(medalRowSkins[index])) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Bộ mặt của một row. Row của chính người chơi được ưu tiên trước huy chương — đứng nhất thì vẫn phải nhận ra ngay
        /// đâu là mình. Không khai báo skin nào khớp thì trả null và row quay về cách tô màu cũ.
        /// </summary>
        public LeaderboardRowSkin RowSkin(int medalIndex, bool isLocalPlayer)
        {
            if (isLocalPlayer && IsDeclared(localRowSkin)) return localRowSkin;
            if (medalIndex >= 0 && medalRowSkins != null && medalIndex < medalRowSkins.Length && IsDeclared(medalRowSkins[medalIndex]))
            {
                return medalRowSkins[medalIndex];
            }
            return IsDeclared(defaultRowSkin) ? defaultRowSkin : null;
        }

        /// <summary>
        /// Unity luôn tạo sẵn instance cho field class [Serializable], nên "chưa khai báo" không bao giờ là null — skin chỉ được
        /// tính khi đã gán nền. Nhờ vậy theme cũ (không đụng tới skin) giữ nguyên cách tô màu.
        /// </summary>
        private static bool IsDeclared(LeaderboardRowSkin skin)
        {
            return skin != null && skin.Background != null;
        }

        /// <summary>Màu badge theo chỉ số huy chương; -1 hoặc vượt số màu = màu nghỉ.</summary>
        public Color MedalColor(int medalIndex)
        {
            if (medalIndex < 0 || medalColors == null || medalIndex >= medalColors.Length) return badgeIdleColor;
            return medalColors[medalIndex];
        }

        public Color TierColor(RankTier tier)
        {
            return tier == RankTier.FirstPlace ? firstPlaceColor : podiumColor;
        }
    }
}
