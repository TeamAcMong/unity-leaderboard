using System;

namespace DreamTech.Leaderboard.League
{
    /// <summary>Kết quả mùa của người chơi — 4 popup trong design: Rank Up / Rank Down / Unchanged / Reseted.</summary>
    public enum SeasonOutcome
    {
        Unchanged = 0,
        Promoted = 1,
        Demoted = 2,
        Reset = 3,
    }

    /// <summary>Đầu vào của luật kết thúc mùa.</summary>
    public readonly struct SeasonOutcomeInput
    {
        public SeasonOutcomeInput(int tierIndex, int finalRank, int groupSize, long finalTrophies, bool participated)
        {
            TierIndex = tierIndex;
            FinalRank = finalRank;
            GroupSize = groupSize;
            FinalTrophies = finalTrophies;
            Participated = participated;
        }

        public int TierIndex { get; }

        /// <summary>Hạng 0-based cuối mùa; -1 nếu không có trong bảng.</summary>
        public int FinalRank { get; }

        public int GroupSize { get; }
        public long FinalTrophies { get; }

        /// <summary>Người chơi có kiếm cúp trong mùa không.</summary>
        public bool Participated { get; }
    }

    /// <summary>Quyết định của luật: kết quả + bậc của mùa sau.</summary>
    public readonly struct SeasonOutcomeDecision
    {
        public SeasonOutcomeDecision(SeasonOutcome outcome, int nextTierIndex)
        {
            Outcome = outcome;
            NextTierIndex = nextTierIndex;
        }

        public SeasonOutcome Outcome { get; }
        public int NextTierIndex { get; }
    }

    /// <summary>
    /// Kết quả một mùa đã khép lại. Hai cờ tách riêng vì hai bước UI tách riêng: popup kết quả (<see cref="Acknowledged"/>) và
    /// nhận rương (<see cref="RewardClaimed"/>). App bị tắt giữa hai bước thì lần mở sau vẫn còn rương để nhận.
    /// </summary>
    public sealed class SeasonResult
    {
        public SeasonResult(string seasonId, int tierIndexBefore, int tierIndexAfter, SeasonOutcome outcome, int finalRank,
                            int groupSize, long finalTrophies, LeagueRewardPackage reward, bool acknowledged, bool rewardClaimed)
        {
            if (string.IsNullOrEmpty(seasonId)) throw new ArgumentException("Season id không được rỗng.", nameof(seasonId));
            SeasonId = seasonId;
            TierIndexBefore = tierIndexBefore;
            TierIndexAfter = tierIndexAfter;
            Outcome = outcome;
            FinalRank = finalRank;
            GroupSize = groupSize;
            FinalTrophies = finalTrophies;
            Reward = reward ?? LeagueRewardPackage.None;
            Acknowledged = acknowledged;
            RewardClaimed = rewardClaimed;
        }

        public string SeasonId { get; }
        public int TierIndexBefore { get; }
        public int TierIndexAfter { get; }
        public SeasonOutcome Outcome { get; }
        public int FinalRank { get; }
        public int GroupSize { get; }
        public long FinalTrophies { get; }
        public LeagueRewardPackage Reward { get; }
        public bool Acknowledged { get; }
        public bool RewardClaimed { get; }

        /// <summary>Còn việc cho UI: chưa xem kết quả, hoặc còn rương chưa nhận.</summary>
        public bool IsPending => !Acknowledged || HasUnclaimedReward;

        public bool HasUnclaimedReward => !RewardClaimed && !Reward.IsEmpty;

        public SeasonResult WithAcknowledged()
        {
            return new SeasonResult(SeasonId, TierIndexBefore, TierIndexAfter, Outcome, FinalRank, GroupSize, FinalTrophies, Reward,
                                    true, RewardClaimed);
        }

        public SeasonResult WithRewardClaimed()
        {
            return new SeasonResult(SeasonId, TierIndexBefore, TierIndexAfter, Outcome, FinalRank, GroupSize, FinalTrophies, Reward,
                                    Acknowledged, true);
        }

        public override string ToString()
        {
            return SeasonId + " " + Outcome + " tier " + TierIndexBefore + "→" + TierIndexAfter + " rank #" + (FinalRank + 1) + "/" +
                   GroupSize + " trophies " + FinalTrophies;
        }
    }
}
