using UnityEngine;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>Mọi chữ hiển thị cho người chơi (để localize). Logic không hardcode chuỗi nào.</summary>
    [CreateAssetMenu(menuName = "DreamTech/Leaderboard/Text Config", fileName = "LeaderboardTextConfig")]
    public sealed class LeaderboardTextConfig : ScriptableObject
    {
        [SerializeField] private string title = "LEADERBOARD";
        [SerializeField] private string loading = "Loading";
        [SerializeField] private string errorMessage = "Couldn't load the leaderboard";
        [SerializeField] private string retry = "Retry";
        [SerializeField] private string empty = "No players yet";
        [SerializeField] private string firstPlaceTitle = "YOU'RE #1!";
        [SerializeField] private string podiumTitle = "TOP 3!";
        [Tooltip("{0} = hạng (1-based).")]
        [SerializeField] private string rankFormat = "Rank #{0}";
        [SerializeField] private string newPill = "NEW";
        [SerializeField] private string bestPill = "BEST";
        [SerializeField] private string gap = ". . .";
        [Tooltip("Tên hiện cho người chơi. Để trống = dùng tên backend trả về.")]
        [SerializeField] private string localPlayerName = "";
        [Tooltip("Tên thay thế khi backend trả tên rỗng.")]
        [SerializeField] private string fallbackPlayerName = "Player";
        [Tooltip("Chuỗi định dạng .NET cho số điểm trên row (InvariantCulture). \"N0\" (mặc định) = 1,234; \"0\" = 1234. " +
                 "Để trống = \"N0\".")]
        [SerializeField] private string scoreFormat = DefaultScoreFormat;

        /// <summary>Định dạng điểm của mọi bản trước khi có <see cref="ScoreFormat"/> — có dấu phân cách hàng nghìn.</summary>
        public const string DefaultScoreFormat = "N0";

        public string Title => title;
        public string Loading => loading;
        public string ErrorMessage => errorMessage;
        public string Retry => retry;
        public string Empty => empty;
        public string FirstPlaceTitle => firstPlaceTitle;
        public string PodiumTitle => podiumTitle;
        public string RankFormat => rankFormat;
        public string NewPill => newPill;
        public string BestPill => bestPill;
        public string Gap => gap;
        public string LocalPlayerName => localPlayerName;
        public string FallbackPlayerName => fallbackPlayerName;
        public string ScoreFormat => string.IsNullOrEmpty(scoreFormat) ? DefaultScoreFormat : scoreFormat;
    }
}
