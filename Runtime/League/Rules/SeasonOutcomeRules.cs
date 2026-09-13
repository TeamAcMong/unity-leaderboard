using System;

namespace DreamTech.Leaderboard.League
{
    /// <summary>Luật kết thúc mùa: lên, xuống, giữ hay reset.</summary>
    public interface ISeasonOutcomeRule
    {
        SeasonOutcomeDecision Decide(in SeasonOutcomeInput input, LeagueLadder ladder, ILeagueZoneRule zoneRule);
    }

    /// <summary>
    /// Mặc định: theo vùng cuối mùa. Người không chơi cả mùa giữ nguyên bậc, trừ khi bật reset người vắng mặt
    /// (<paramref name="inactiveResetTierIndex"/> &gt;= 0 → về bậc đó, kết quả <see cref="SeasonOutcome.Reset"/>).
    /// </summary>
    public sealed class ZoneSeasonOutcomeRule : ISeasonOutcomeRule
    {
        public const int NoInactiveReset = -1;

        public ZoneSeasonOutcomeRule(int inactiveResetTierIndex = NoInactiveReset)
        {
            InactiveResetTierIndex = inactiveResetTierIndex < 0 ? NoInactiveReset : inactiveResetTierIndex;
        }

        public int InactiveResetTierIndex { get; }

        public SeasonOutcomeDecision Decide(in SeasonOutcomeInput input, LeagueLadder ladder, ILeagueZoneRule zoneRule)
        {
            if (ladder == null) throw new ArgumentNullException(nameof(ladder));
            if (zoneRule == null) throw new ArgumentNullException(nameof(zoneRule));
            int tierIndex = ladder.ClampIndex(input.TierIndex);

            if (!input.Participated)
            {
                return InactiveResetTierIndex == NoInactiveReset
                    ? new SeasonOutcomeDecision(SeasonOutcome.Unchanged, tierIndex)
                    : new SeasonOutcomeDecision(SeasonOutcome.Reset, ladder.ClampIndex(InactiveResetTierIndex));
            }

            LeagueZone zone = zoneRule.GetBands(ladder, tierIndex, input.GroupSize).ZoneOf(input.FinalRank);
            if (zone == LeagueZone.Promotion && !ladder.IsTop(tierIndex))
            {
                return new SeasonOutcomeDecision(SeasonOutcome.Promoted, tierIndex + 1);
            }
            if (zone == LeagueZone.Demotion && !ladder.IsBottom(tierIndex))
            {
                return new SeasonOutcomeDecision(SeasonOutcome.Demoted, tierIndex - 1);
            }
            return new SeasonOutcomeDecision(SeasonOutcome.Unchanged, tierIndex);
        }
    }
}
