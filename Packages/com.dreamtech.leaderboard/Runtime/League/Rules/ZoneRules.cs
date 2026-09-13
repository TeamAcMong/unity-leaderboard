namespace DreamTech.Leaderboard.League
{
    /// <summary>Luật vùng lên/xuống hạng. Cắm bản khác nếu game muốn chia theo % nhóm, theo điểm sàn...</summary>
    public interface ILeagueZoneRule
    {
        LeagueZoneBands GetBands(LeagueLadder ladder, int tierIndex, int groupSize);
    }

    /// <summary>
    /// Mặc định: số người lên/xuống cố định theo từng tier (<see cref="LeagueTierDefinition"/>). Tier cao nhất không có vùng lên,
    /// tier thấp nhất không có vùng xuống.
    /// </summary>
    public sealed class CountLeagueZoneRule : ILeagueZoneRule
    {
        public LeagueZoneBands GetBands(LeagueLadder ladder, int tierIndex, int groupSize)
        {
            if (ladder == null) throw new System.ArgumentNullException(nameof(ladder));
            int clamped = ladder.ClampIndex(tierIndex);
            LeagueTierDefinition tier = ladder.TierAt(clamped);
            int promotion = ladder.IsTop(clamped) ? 0 : tier.PromotionCount;
            int demotion = ladder.IsBottom(clamped) ? 0 : tier.DemotionCount;
            return new LeagueZoneBands(groupSize, promotion, demotion);
        }
    }
}
