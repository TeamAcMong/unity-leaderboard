using UnityEngine;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>Dữ liệu giả lập cho <see cref="MockLeaderboardService"/> — chỉnh phân bố bot và tên mẫu trong inspector.</summary>
    [CreateAssetMenu(menuName = "DreamTech/Leaderboard/Mock Backend Config", fileName = "MockLeaderboardConfig")]
    public sealed class MockLeaderboardConfig : ScriptableObject
    {
        [SerializeField, Min(0)] private int botCount = 2000;
        [SerializeField] private int seed = 7;
        [SerializeField] private string localPlayerId = "local-player";
        [SerializeField] private string localDisplayName = "You";
        [SerializeField] private long minimumScore = 300;
        [SerializeField] private long maximumScore = 250000;
        [Tooltip("Số mũ phân bố điểm: 3 = top rất thưa, nhiều người điểm thấp.")]
        [SerializeField, Min(0.1f)] private float distributionExponent = 3f;
        [SerializeField, Min(0)] private int latencyMilliseconds = 200;
        [SerializeField] private string seasonKey = "mock-season";
        [Tooltip("Tên cố định xen vào danh sách bot (tên tiếng Việt, tên rất dài...) để kiểm hiển thị.")]
        [SerializeField] private string[] featuredNames = new string[0];
        [SerializeField, Min(1)] private int featuredNameEvery = 3;

        public MockLeaderboardOptions CreateOptions()
        {
            var options = new MockLeaderboardOptions
            {
                BotCount = botCount,
                Seed = seed,
                LocalPlayerId = localPlayerId,
                LocalDisplayName = localDisplayName,
                MinScore = minimumScore,
                MaxScore = maximumScore,
                DistributionExponent = distributionExponent,
                LatencyMilliseconds = latencyMilliseconds,
                SeasonKey = seasonKey,
                FeaturedNameEvery = featuredNameEvery,
            };
            if (featuredNames != null) options.FeaturedNames = (string[])featuredNames.Clone();
            return options;
        }
    }
}
