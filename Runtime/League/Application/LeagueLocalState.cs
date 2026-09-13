using System;
using System.Collections.Generic;
using System.Globalization;

namespace DreamTech.Leaderboard.League
{
    /// <summary>Quà đã chốt phát nhưng game chưa nhận được (granter trả false hoặc app tắt giữa chừng).</summary>
    internal sealed class PendingLeagueReward
    {
        public PendingLeagueReward(string grantId, LeagueRewardPackage package)
        {
            GrantId = grantId;
            Package = package;
        }

        public string GrantId { get; }
        public LeagueRewardPackage Package { get; }
    }

    /// <summary>
    /// Phần trạng thái League nằm trên máy người chơi, không phụ thuộc dịch vụ nhóm: streak, cúp chờ gửi, quà chờ phát.
    /// Thay dịch vụ nhóm (mô phỏng → backend) không đụng tới phần này.
    /// </summary>
    internal sealed class LeagueLocalState
    {
        private const int CurrentFormat = 1;

        public WinStreakState Streak;

        /// <summary>Tăng mỗi lần streak bị tụt, để quà của một bậc streak chỉ phát một lần mỗi lượt streak.</summary>
        public int StreakRunNumber;

        public long NextGrantSerial;
        public readonly List<LeagueTrophyGrant> PendingTrophyGrants = new List<LeagueTrophyGrant>();
        public readonly List<PendingLeagueReward> PendingRewards = new List<PendingLeagueReward>();

        public string NextGrantId(string kind)
        {
            NextGrantSerial++;
            return kind + "-" + NextGrantSerial.ToString(CultureInfo.InvariantCulture) + "-" +
                   Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        public string Encode()
        {
            var record = new LeagueTextRecord(CurrentFormat);
            record.SetInt("streak.level", Streak.Level);
            record.SetInt("streak.wins", Streak.WinsTowardNextLevel);
            record.SetInt("streak.run", StreakRunNumber);
            record.SetLong("grant.serial", NextGrantSerial);

            record.SetInt("trophyGrants", PendingTrophyGrants.Count);
            for (int index = 0; index < PendingTrophyGrants.Count; index++)
            {
                string prefix = "trophyGrant" + index.ToString(CultureInfo.InvariantCulture);
                record.SetString(prefix + ".id", PendingTrophyGrants[index].GrantId);
                record.SetString(prefix + ".season", PendingTrophyGrants[index].SeasonId);
                record.SetInt(prefix + ".trophies", PendingTrophyGrants[index].Trophies);
            }

            record.SetInt("rewards", PendingRewards.Count);
            for (int index = 0; index < PendingRewards.Count; index++)
            {
                string prefix = "reward" + index.ToString(CultureInfo.InvariantCulture);
                record.SetString(prefix + ".id", PendingRewards[index].GrantId);
                record.SetPackage(prefix + ".package", PendingRewards[index].Package);
            }
            return record.Encode();
        }

        /// <summary>Chuỗi hỏng hoặc khác định dạng thì trả trạng thái rỗng — mất streak tốt hơn làm game crash lúc khởi động.</summary>
        public static LeagueLocalState Decode(string text)
        {
            var state = new LeagueLocalState();
            if (!LeagueTextRecord.TryDecode(text, out LeagueTextRecord record) || record.Format != CurrentFormat) return state;

            state.Streak = new WinStreakState(record.GetInt("streak.level", 0), record.GetInt("streak.wins", 0));
            state.StreakRunNumber = record.GetInt("streak.run", 0);
            state.NextGrantSerial = record.GetLong("grant.serial", 0);

            int grantCount = record.GetInt("trophyGrants", 0);
            for (int index = 0; index < grantCount; index++)
            {
                string prefix = "trophyGrant" + index.ToString(CultureInfo.InvariantCulture);
                string grantId = record.GetString(prefix + ".id", string.Empty);
                string seasonId = record.GetString(prefix + ".season", string.Empty);
                int trophies = record.GetInt(prefix + ".trophies", 0);
                if (grantId.Length > 0 && seasonId.Length > 0 && trophies > 0)
                {
                    state.PendingTrophyGrants.Add(new LeagueTrophyGrant(grantId, seasonId, trophies));
                }
            }

            int rewardCount = record.GetInt("rewards", 0);
            for (int index = 0; index < rewardCount; index++)
            {
                string prefix = "reward" + index.ToString(CultureInfo.InvariantCulture);
                string grantId = record.GetString(prefix + ".id", string.Empty);
                LeagueRewardPackage package = record.GetPackage(prefix + ".package");
                if (grantId.Length > 0 && !package.IsEmpty) state.PendingRewards.Add(new PendingLeagueReward(grantId, package));
            }
            return state;
        }
    }
}
