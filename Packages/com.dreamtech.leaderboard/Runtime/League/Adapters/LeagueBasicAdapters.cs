using System;
using System.Collections.Generic;

namespace DreamTech.Leaderboard.League
{
    /// <summary>Giờ của máy.</summary>
    public sealed class SystemLeagueClock : ILeagueClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    /// <summary>Giờ do code đặt (test).</summary>
    public sealed class ManualLeagueClock : ILeagueClock
    {
        public ManualLeagueClock(DateTime utcNow)
        {
            UtcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        }

        public DateTime UtcNow { get; private set; }

        public void Set(DateTime utcNow)
        {
            UtcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        }

        public void Advance(TimeSpan duration)
        {
            UtcNow += duration;
        }
    }

    /// <summary>Bọc một đồng hồ khác và cộng thêm độ lệch — cheat "tua tới cuối mùa" trong build thật.</summary>
    public sealed class OffsetLeagueClock : ILeagueClock
    {
        private readonly ILeagueClock _inner;

        public OffsetLeagueClock(ILeagueClock inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public TimeSpan Offset { get; set; }
        public DateTime UtcNow => _inner.UtcNow + Offset;

        public void Advance(TimeSpan duration)
        {
            Offset += duration;
        }
    }

    /// <summary>Lưu trong RAM — mặc định khi chưa cắm nơi lưu thật, và cho test.</summary>
    public sealed class InMemoryLeagueTextStore : ILeagueTextStore
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);

        public bool TryRead(string key, out string value)
        {
            return _values.TryGetValue(key, out value);
        }

        public void Write(string key, string value)
        {
            _values[key] = value ?? string.Empty;
        }

        public void Delete(string key)
        {
            _values.Remove(key);
        }
    }

    /// <summary>Chưa cắm kho đồ: mọi gói quà nằm chờ, không mất. Cắm granter thật rồi gọi <see cref="LeagueSystem.GrantPendingRewards"/>.</summary>
    public sealed class DeferredLeagueRewardGranter : ILeagueRewardGranter
    {
        public bool TryGrant(string grantId, LeagueRewardPackage package)
        {
            return false;
        }
    }

    /// <summary>Granter bọc một hàm của game — cách lắp nhanh nhất.</summary>
    public sealed class DelegateLeagueRewardGranter : ILeagueRewardGranter
    {
        private readonly Func<string, LeagueRewardPackage, bool> _grant;

        public DelegateLeagueRewardGranter(Func<string, LeagueRewardPackage, bool> grant)
        {
            _grant = grant ?? throw new ArgumentNullException(nameof(grant));
        }

        public bool TryGrant(string grantId, LeagueRewardPackage package)
        {
            return _grant(grantId, package);
        }
    }

    /// <summary>Cổng bật/tắt bằng code (cheat, test).</summary>
    public sealed class ManualLeagueFeatureGate : ILeagueFeatureGate
    {
        public ManualLeagueFeatureGate(bool isUnlocked)
        {
            IsUnlocked = isUnlocked;
        }

        public bool IsUnlocked { get; set; }
    }

    /// <summary>Cổng bọc một hàm của game (vd "level hiện tại >= 11").</summary>
    public sealed class DelegateLeagueFeatureGate : ILeagueFeatureGate
    {
        private readonly Func<bool> _isUnlocked;

        public DelegateLeagueFeatureGate(Func<bool> isUnlocked)
        {
            _isUnlocked = isUnlocked ?? throw new ArgumentNullException(nameof(isUnlocked));
        }

        public bool IsUnlocked => _isUnlocked();
    }
}
