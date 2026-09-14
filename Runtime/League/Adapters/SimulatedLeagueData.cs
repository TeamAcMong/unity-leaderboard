using System;
using System.Collections.Generic;
using System.Globalization;

namespace DreamTech.Leaderboard.League
{
    /// <summary>
    /// Sổ của một mùa đã khép: đủ để tính lại kết quả khi có grant tới muộn (cửa sổ mùa + bậc + cúp) và để gửi lại grant cũ không
    /// bị cộng trùng (id các grant đã tính). Mỗi id mùa chỉ có một sổ — đó là thứ bảo đảm không bao giờ có hai kết quả cùng mùa.
    /// </summary>
    internal sealed class SimulatedClosedSeason
    {
        public SimulatedClosedSeason(string seasonId, DateTime startUtc, DateTime endUtc, int tierIndex)
        {
            if (string.IsNullOrEmpty(seasonId)) throw new ArgumentException("Season id không được rỗng.", nameof(seasonId));
            if (endUtc <= startUtc) throw new ArgumentException("Sổ mùa phải có cửa sổ mùa hợp lệ.", nameof(endUtc));
            SeasonId = seasonId;
            StartUtc = startUtc;
            EndUtc = endUtc;
            TierIndex = tierIndex;
            NextTierIndex = tierIndex;
        }

        public string SeasonId { get; }
        public DateTime StartUtc { get; }
        public DateTime EndUtc { get; }

        /// <summary>Bậc người chơi trong mùa này.</summary>
        public int TierIndex;

        /// <summary>Bậc của mùa kế tiếp theo kết quả của mùa này.</summary>
        public int NextTierIndex;

        public long LocalTrophies;
        public readonly HashSet<string> AppliedGrantIds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>null khi mùa không để lại việc gì cho UI (không chơi và bậc không đổi).</summary>
        public SeasonResult Result;

        public SeasonWindow Window => new SeasonWindow(SeasonId, StartUtc, EndUtc);

        /// <summary>Kết quả đã chốt với người chơi (đã xem, hoặc đã nhận rương): không được tính lại nữa.</summary>
        public bool IsFinalized => Result != null && (Result.Acknowledged || Result.RewardClaimed);

        public bool HasPendingResult => Result != null && Result.IsPending;
    }

    /// <summary>Phần dữ liệu mô phỏng lưu trên máy.</summary>
    internal sealed class SimulatedLeagueData
    {
        /// <summary>
        /// Định dạng duy nhất đọc được (từ 0.2.1). Định dạng 1 của 0.2.0 bị coi là không dùng được và bắt đầu lại từ đầu: League chưa
        /// phát hành cho người chơi, còn save 0.2.0 trên máy dev/QA có thể đã hỏng bởi lỗi "đồng hồ lùi" (mùa đang giữ nằm ở tương
        /// lai, kết quả trùng mùa) theo những cách không chuyển đổi an toàn được.
        /// </summary>
        private const int CurrentFormat = 2;

        public int TierIndex;
        public string ActiveSeasonId;
        public DateTime ActiveSeasonStart;
        public DateTime ActiveSeasonEnd;
        public int ActiveTierIndex;
        public long LocalTrophies;
        public readonly HashSet<string> AppliedGrantIds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Các mùa đã khép, theo thứ tự thời gian (cũ trước).</summary>
        public readonly List<SimulatedClosedSeason> ClosedSeasons = new List<SimulatedClosedSeason>();

        public bool HasActiveSeason => !string.IsNullOrEmpty(ActiveSeasonId);

        /// <summary>Mùa đã khép mới nhất (phần tử cuối); null nếu chưa khép mùa nào.</summary>
        public SimulatedClosedSeason NewestClosedSeason => ClosedSeasons.Count > 0 ? ClosedSeasons[ClosedSeasons.Count - 1] : null;

        public void ClearActiveSeason()
        {
            ActiveSeasonId = null;
            ActiveSeasonStart = default;
            ActiveSeasonEnd = default;
            ActiveTierIndex = 0;
            LocalTrophies = 0;
            AppliedGrantIds.Clear();
        }

        public SimulatedClosedSeason FindClosedSeason(string seasonId)
        {
            foreach (SimulatedClosedSeason closed in ClosedSeasons)
            {
                if (string.Equals(closed.SeasonId, seasonId, StringComparison.Ordinal)) return closed;
            }
            return null;
        }

        /// <summary>
        /// Thêm sổ mùa đã khép, chèn đúng thứ tự thời gian (theo mốc bắt đầu; cùng mốc thì sau sổ đã có). Trùng id thì GIỮ sổ đã
        /// có (sinh trước) và bỏ sổ mới — trả false.
        /// </summary>
        public bool TryAddClosedSeason(SimulatedClosedSeason closed)
        {
            if (closed == null || FindClosedSeason(closed.SeasonId) != null) return false;
            int insertAt = ClosedSeasons.Count;
            while (insertAt > 0 && ClosedSeasons[insertAt - 1].StartUtc > closed.StartUtc) insertAt--;
            ClosedSeasons.Insert(insertAt, closed);
            return true;
        }

        public string Encode()
        {
            var record = new LeagueTextRecord(CurrentFormat);
            record.SetInt("tier", TierIndex);
            if (HasActiveSeason)
            {
                record.SetString("active.season", ActiveSeasonId);
                record.SetLong("active.start", ActiveSeasonStart.Ticks);
                record.SetLong("active.end", ActiveSeasonEnd.Ticks);
                record.SetInt("active.tier", ActiveTierIndex);
                record.SetLong("active.trophies", LocalTrophies);
                WriteGrantIds(record, "active", AppliedGrantIds);
            }

            record.SetInt("closed", ClosedSeasons.Count);
            for (int index = 0; index < ClosedSeasons.Count; index++)
            {
                SimulatedClosedSeason closed = ClosedSeasons[index];
                string prefix = "closed" + index.ToString(CultureInfo.InvariantCulture);
                record.SetString(prefix + ".season", closed.SeasonId);
                record.SetLong(prefix + ".start", closed.StartUtc.Ticks);
                record.SetLong(prefix + ".end", closed.EndUtc.Ticks);
                record.SetInt(prefix + ".tier", closed.TierIndex);
                record.SetInt(prefix + ".nextTier", closed.NextTierIndex);
                record.SetLong(prefix + ".trophies", closed.LocalTrophies);
                WriteGrantIds(record, prefix, closed.AppliedGrantIds);
                if (closed.Result != null) record.SetResult(prefix + ".result", closed.Result);
            }
            return record.Encode();
        }

        /// <summary>
        /// Chuỗi hỏng hoặc định dạng khác <see cref="CurrentFormat"/> (kể cả định dạng 1 của 0.2.0) → null: dịch vụ bắt đầu lại từ
        /// đầu, không ném lỗi. Sổ mùa thiếu cửa sổ mùa hợp lệ bị bỏ (chuỗi hỏng từng phần).
        /// </summary>
        public static SimulatedLeagueData Decode(string text)
        {
            if (!LeagueTextRecord.TryDecode(text, out LeagueTextRecord record) || record.Format != CurrentFormat) return null;
            var data = new SimulatedLeagueData { TierIndex = record.GetInt("tier", 0) };

            string activeSeasonId = record.GetString("active.season", string.Empty);
            if (activeSeasonId.Length > 0 && TryReadWindow(record, "active", out DateTime activeStart, out DateTime activeEnd))
            {
                data.ActiveSeasonId = activeSeasonId;
                data.ActiveSeasonStart = activeStart;
                data.ActiveSeasonEnd = activeEnd;
                data.ActiveTierIndex = record.GetInt("active.tier", data.TierIndex);
                data.LocalTrophies = Math.Max(0, record.GetLong("active.trophies", 0));
                ReadGrantIds(record, "active", data.AppliedGrantIds);
            }

            ReadClosedSeasons(record, data);
            return data;
        }

        private static void ReadClosedSeasons(LeagueTextRecord record, SimulatedLeagueData data)
        {
            int closedCount = record.GetInt("closed", 0);
            for (int index = 0; index < closedCount; index++)
            {
                string prefix = "closed" + index.ToString(CultureInfo.InvariantCulture);
                string seasonId = record.GetString(prefix + ".season", string.Empty);
                if (seasonId.Length == 0 || !TryReadWindow(record, prefix, out DateTime startUtc, out DateTime endUtc)) continue;

                int tierIndex = record.GetInt(prefix + ".tier", data.TierIndex);
                var closed = new SimulatedClosedSeason(seasonId, startUtc, endUtc, tierIndex)
                {
                    NextTierIndex = record.GetInt(prefix + ".nextTier", tierIndex),
                    LocalTrophies = Math.Max(0, record.GetLong(prefix + ".trophies", 0)),
                };
                ReadGrantIds(record, prefix, closed.AppliedGrantIds);

                SeasonResult result = record.GetResult(prefix + ".result");
                if (result != null && string.Equals(result.SeasonId, seasonId, StringComparison.Ordinal)) closed.Result = result;
                data.TryAddClosedSeason(closed);
            }
        }

        private static bool TryReadWindow(LeagueTextRecord record, string prefix, out DateTime startUtc, out DateTime endUtc)
        {
            long startTicks = record.GetLong(prefix + ".start", 0);
            long endTicks = record.GetLong(prefix + ".end", 0);
            if (endTicks > startTicks && startTicks >= DateTime.MinValue.Ticks && endTicks <= DateTime.MaxValue.Ticks)
            {
                startUtc = new DateTime(startTicks, DateTimeKind.Utc);
                endUtc = new DateTime(endTicks, DateTimeKind.Utc);
                return true;
            }
            startUtc = DateTime.MinValue;
            endUtc = DateTime.MinValue;
            return false;
        }

        private static void WriteGrantIds(LeagueTextRecord record, string prefix, HashSet<string> grantIds)
        {
            var sorted = new List<string>(grantIds);
            sorted.Sort(StringComparer.Ordinal);
            record.SetInt(prefix + ".grants", sorted.Count);
            for (int index = 0; index < sorted.Count; index++)
            {
                record.SetString(prefix + ".grant" + index.ToString(CultureInfo.InvariantCulture), sorted[index]);
            }
        }

        private static void ReadGrantIds(LeagueTextRecord record, string prefix, HashSet<string> grantIds)
        {
            int grantCount = record.GetInt(prefix + ".grants", 0);
            for (int index = 0; index < grantCount; index++)
            {
                string grantId = record.GetString(prefix + ".grant" + index.ToString(CultureInfo.InvariantCulture), string.Empty);
                if (grantId.Length > 0) grantIds.Add(grantId);
            }
        }
    }
}
