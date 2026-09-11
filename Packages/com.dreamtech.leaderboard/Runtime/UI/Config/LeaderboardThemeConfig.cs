using UnityEngine;

namespace DreamTech.Leaderboard.UI
{
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
