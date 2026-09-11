using UnityEngine;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>Tham số lắp ráp một board (id, lượng dữ liệu tải, số người diễn vượt, số hạng bục vinh quang).</summary>
    [CreateAssetMenu(menuName = "DreamTech/Leaderboard/Board Config", fileName = "LeaderboardBoardConfig")]
    public sealed class LeaderboardBoardConfig : ScriptableObject
    {
        [SerializeField] private string boardId = "main";
        [Tooltip("Số hạng đầu luôn tải. Giải đấu nhóm ~50 người thì đặt 50.")]
        [SerializeField, Min(0)] private int topCount = FetchWindowSettings.DefaultTopCount;
        [SerializeField, Min(0)] private int rowsAbove = FetchWindowSettings.DefaultRowsAbove;
        [SerializeField, Min(0)] private int rowsBelow = FetchWindowSettings.DefaultRowsBelow;
        [Tooltip("Số người tối đa được diễn vượt từng người; nhảy nhiều hơn thì quay số trước.")]
        [SerializeField, Min(0)] private int maximumAnimatedPasses = FetchWindowSettings.DefaultMaxAnimatedPasses;
        [SerializeField, Min(1)] private int podiumSize = RankTierRule.DefaultPodiumSize;

        public string BoardId => boardId;

        public LeaderboardBoardSettings CreateSettings()
        {
            return new LeaderboardBoardSettings(boardId,
                new FetchWindowSettings(topCount, rowsAbove, rowsBelow, maximumAnimatedPasses),
                new RankTierRule(podiumSize));
        }
    }
}
