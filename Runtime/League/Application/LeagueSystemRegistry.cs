using System;
using System.Collections.Generic;

namespace DreamTech.Leaderboard.League
{
    /// <summary>
    /// Chỗ gặp nhau giữa các assembly, cùng hợp đồng với <see cref="LeaderboardBoardRegistry"/>: composition root đăng ký,
    /// host (màn League, màn Win, popup thoát, cheat) tra theo id. Chỉ dùng trên main thread; Reset trước khi reload assembly.
    /// </summary>
    public static class LeagueSystemRegistry
    {
        private static readonly Dictionary<string, LeagueSystem> Systems = new Dictionary<string, LeagueSystem>(StringComparer.Ordinal);

        public static event Action<LeagueSystem> SystemRegistered;

        public static void Register(LeagueSystem system)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));
            Systems[system.SystemId] = system;
            SystemRegistered?.Invoke(system);
        }

        /// <summary>Chỉ gỡ khi đúng tham chiếu đang đăng ký.</summary>
        public static bool Unregister(LeagueSystem system)
        {
            if (system == null) return false;
            if (!Systems.TryGetValue(system.SystemId, out LeagueSystem existing) || !ReferenceEquals(existing, system)) return false;
            return Systems.Remove(system.SystemId);
        }

        public static bool TryGet(string systemId, out LeagueSystem system)
        {
            if (systemId == null)
            {
                system = null;
                return false;
            }
            return Systems.TryGetValue(systemId, out system);
        }

        public static void Reset()
        {
            Systems.Clear();
            SystemRegistered = null;
        }
    }
}
