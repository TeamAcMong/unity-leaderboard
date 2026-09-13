using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DreamTech.Leaderboard.League
{
    /// <summary>
    /// Bản ghi khoá=giá trị mã hoá thành chuỗi nhiều dòng. Không dùng JsonUtility vì assembly này không tham chiếu Unity;
    /// định dạng dòng đọc được bằng mắt khi debug PlayerPrefs. Mỗi bản ghi có <c>format</c> để đổi định dạng về sau.
    /// </summary>
    internal sealed class LeagueTextRecord
    {
        private const string FormatKey = "format";
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);

        public LeagueTextRecord(int format)
        {
            SetInt(FormatKey, format);
        }

        private LeagueTextRecord()
        {
        }

        public int Format => GetInt(FormatKey, 0);

        public static bool TryDecode(string text, out LeagueTextRecord record)
        {
            record = null;
            if (string.IsNullOrEmpty(text)) return false;
            var decoded = new LeagueTextRecord();
            string[] lines = text.Split('\n');
            foreach (string line in lines)
            {
                if (line.Length == 0) continue;
                int separator = line.IndexOf('=');
                if (separator <= 0) return false;
                if (!TryUnescape(line.Substring(separator + 1), out string value)) return false;
                decoded._values[line.Substring(0, separator)] = value;
            }
            if (!decoded._values.ContainsKey(FormatKey)) return false;
            record = decoded;
            return true;
        }

        public string Encode()
        {
            var builder = new StringBuilder();
            var keys = new List<string>(_values.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                builder.Append(key).Append('=').Append(Escape(_values[key])).Append('\n');
            }
            return builder.ToString();
        }

        public bool Has(string key)
        {
            return _values.ContainsKey(key);
        }

        public void SetString(string key, string value)
        {
            ValidateKey(key);
            _values[key] = value ?? string.Empty;
        }

        public string GetString(string key, string fallback)
        {
            return _values.TryGetValue(key, out string value) ? value : fallback;
        }

        public void SetInt(string key, int value)
        {
            SetString(key, value.ToString(CultureInfo.InvariantCulture));
        }

        public int GetInt(string key, int fallback)
        {
            return _values.TryGetValue(key, out string value) &&
                   int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;
        }

        public void SetLong(string key, long value)
        {
            SetString(key, value.ToString(CultureInfo.InvariantCulture));
        }

        public long GetLong(string key, long fallback)
        {
            return _values.TryGetValue(key, out string value) &&
                   long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
                ? parsed
                : fallback;
        }

        public void SetBool(string key, bool value)
        {
            SetString(key, value ? "1" : "0");
        }

        public bool GetBool(string key, bool fallback)
        {
            if (!_values.TryGetValue(key, out string value)) return fallback;
            return value == "1";
        }

        // ---------------------------------------------------------------- Kiểu phức hợp dùng chung

        public void SetPackage(string prefix, LeagueRewardPackage package)
        {
            package = package ?? LeagueRewardPackage.None;
            SetString(prefix + ".chest", package.ChestId);
            SetInt(prefix + ".items", package.Items.Count);
            for (int index = 0; index < package.Items.Count; index++)
            {
                string itemPrefix = prefix + ".item" + index.ToString(CultureInfo.InvariantCulture);
                SetString(itemPrefix + ".id", package.Items[index].ItemId);
                SetInt(itemPrefix + ".amount", package.Items[index].Amount);
            }
        }

        public LeagueRewardPackage GetPackage(string prefix)
        {
            int count = GetInt(prefix + ".items", 0);
            string chestId = GetString(prefix + ".chest", string.Empty);
            if (count <= 0) return chestId.Length == 0 ? LeagueRewardPackage.None : new LeagueRewardPackage(chestId, null);

            var items = new List<LeagueRewardItem>(count);
            for (int index = 0; index < count; index++)
            {
                string itemPrefix = prefix + ".item" + index.ToString(CultureInfo.InvariantCulture);
                string itemId = GetString(itemPrefix + ".id", string.Empty);
                int amount = GetInt(itemPrefix + ".amount", 0);
                if (itemId.Length > 0 && amount > 0) items.Add(new LeagueRewardItem(itemId, amount));
            }
            return new LeagueRewardPackage(chestId, items);
        }

        public void SetResult(string prefix, SeasonResult result)
        {
            SetString(prefix + ".season", result.SeasonId);
            SetInt(prefix + ".tierBefore", result.TierIndexBefore);
            SetInt(prefix + ".tierAfter", result.TierIndexAfter);
            SetInt(prefix + ".outcome", (int)result.Outcome);
            SetInt(prefix + ".rank", result.FinalRank);
            SetInt(prefix + ".groupSize", result.GroupSize);
            SetLong(prefix + ".trophies", result.FinalTrophies);
            SetBool(prefix + ".acknowledged", result.Acknowledged);
            SetBool(prefix + ".claimed", result.RewardClaimed);
            SetPackage(prefix + ".reward", result.Reward);
        }

        public SeasonResult GetResult(string prefix)
        {
            string seasonId = GetString(prefix + ".season", string.Empty);
            if (seasonId.Length == 0) return null;
            return new SeasonResult(seasonId, GetInt(prefix + ".tierBefore", 0), GetInt(prefix + ".tierAfter", 0),
                                    (SeasonOutcome)GetInt(prefix + ".outcome", 0), GetInt(prefix + ".rank", -1),
                                    GetInt(prefix + ".groupSize", 0), GetLong(prefix + ".trophies", 0), GetPackage(prefix + ".reward"),
                                    GetBool(prefix + ".acknowledged", false), GetBool(prefix + ".claimed", false));
        }

        // ---------------------------------------------------------------- Nội bộ

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Khoá không được rỗng.", nameof(key));
            if (key.IndexOf('=') >= 0 || key.IndexOf('\n') >= 0 || key.IndexOf('\r') >= 0)
            {
                throw new ArgumentException("Khoá không được chứa '=' hay xuống dòng: " + key, nameof(key));
            }
        }

        private static string Escape(string value)
        {
            if (value.IndexOf('\\') < 0 && value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0) return value;
            var builder = new StringBuilder(value.Length + 8);
            foreach (char character in value)
            {
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    default: builder.Append(character); break;
                }
            }
            return builder.ToString();
        }

        private static bool TryUnescape(string value, out string result)
        {
            if (value.IndexOf('\\') < 0)
            {
                result = value;
                return true;
            }
            var builder = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (character != '\\')
                {
                    builder.Append(character);
                    continue;
                }
                if (index + 1 >= value.Length)
                {
                    result = null;
                    return false;
                }
                char next = value[++index];
                if (next == '\\') builder.Append('\\');
                else if (next == 'n') builder.Append('\n');
                else if (next == 'r') builder.Append('\r');
                else
                {
                    result = null;
                    return false;
                }
            }
            result = builder.ToString();
            return true;
        }
    }
}
