using UnityEngine;

namespace DreamTech.Leaderboard.League.Unity
{
    /// <summary>
    /// Lưu trạng thái League vào PlayerPrefs. Dùng được ngay khi game chưa muốn đụng save system riêng; đổi sang save của game
    /// chỉ là cắm một <see cref="ILeagueTextStore"/> khác.
    /// </summary>
    public sealed class PlayerPrefsLeagueTextStore : ILeagueTextStore
    {
        public const string DefaultKeyPrefix = "dreamtech.league.";

        private readonly string _keyPrefix;

        public PlayerPrefsLeagueTextStore(string keyPrefix = DefaultKeyPrefix)
        {
            _keyPrefix = keyPrefix ?? string.Empty;
        }

        public bool TryRead(string key, out string value)
        {
            value = PlayerPrefs.GetString(_keyPrefix + key, string.Empty);
            return !string.IsNullOrEmpty(value);
        }

        public void Write(string key, string value)
        {
            PlayerPrefs.SetString(_keyPrefix + key, value ?? string.Empty);
            PlayerPrefs.Save();
        }

        public void Delete(string key)
        {
            PlayerPrefs.DeleteKey(_keyPrefix + key);
            PlayerPrefs.Save();
        }
    }
}
