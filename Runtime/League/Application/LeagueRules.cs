using System;

namespace DreamTech.Leaderboard.League
{
    /// <summary>
    /// Bộ luật của một League, dùng chung cho <see cref="LeagueSystem"/> (vẽ dải zone, rương từng hạng) và cho dịch vụ mô phỏng
    /// (khép mùa). Hai bên đọc cùng một instance nên dải trên màn hình và kết quả thật không thể lệch nhau.
    /// Để null luật nào thì dùng bản mặc định.
    /// </summary>
    public sealed class LeagueRules
    {
        public LeagueRules(LeagueLadder ladder, ILeagueZoneRule zoneRule = null, ISeasonOutcomeRule outcomeRule = null,
                           ILeagueRewardTable rewardTable = null)
        {
            Ladder = ladder ?? throw new ArgumentNullException(nameof(ladder));
            ZoneRule = zoneRule ?? new CountLeagueZoneRule();
            OutcomeRule = outcomeRule ?? new ZoneSeasonOutcomeRule();
            RewardTable = rewardTable ?? new EmptyLeagueRewardTable();
        }

        public LeagueLadder Ladder { get; }
        public ILeagueZoneRule ZoneRule { get; }
        public ISeasonOutcomeRule OutcomeRule { get; }
        public ILeagueRewardTable RewardTable { get; }

        public LeagueZoneBands BandsFor(int tierIndex, int groupSize)
        {
            return ZoneRule.GetBands(Ladder, tierIndex, groupSize);
        }

        public SeasonOutcomeDecision Decide(in SeasonOutcomeInput input)
        {
            SeasonOutcomeDecision decision = OutcomeRule.Decide(input, Ladder, ZoneRule);
            return new SeasonOutcomeDecision(decision.Outcome, Ladder.ClampIndex(decision.NextTierIndex));
        }

        public LeagueRewardPackage RewardFor(int tierIndex, int rank, int groupSize)
        {
            return RewardTable.RewardFor(tierIndex, rank, groupSize) ?? LeagueRewardPackage.None;
        }
    }
}
