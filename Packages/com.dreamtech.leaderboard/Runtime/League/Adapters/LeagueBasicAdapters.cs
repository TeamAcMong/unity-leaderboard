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

    /// <summary>
    /// Bọc một đồng hồ khác và cộng thêm độ lệch — cheat "tua tới cuối mùa" trong build thật.
    ///
    /// <para>Hai cách dựng: không có nơi lưu thì độ lệch chỉ sống trong RAM (test, demo); có nơi lưu thì độ lệch được nạp lúc
    /// tạo và ghi lại mỗi lần đổi. Cheat tua giờ trong game thật phải dùng bản có nơi lưu: độ lệch mất khi tắt app nghĩa là
    /// giờ của League lùi về giờ thật, dịch vụ nhóm thấy "mùa theo đồng hồ" cũ hơn mùa đang giữ. Muốn chắc chắn League không
    /// bao giờ lùi (kể cả khi người chơi chỉnh giờ máy) thì bọc tiếp bằng <see cref="MonotonicLeagueClock"/>.</para>
    /// </summary>
    public sealed class OffsetLeagueClock : ILeagueClock
    {
        private const int StoreFormat = 1;
        private const string OffsetTicksKey = "offset.ticks";

        /// <summary>
        /// Độ lệch lưu lớn hơn chừng này bị coi là chuỗi hỏng: cheat tua giờ không bao giờ cần tới mười năm, còn giá trị quá lớn
        /// làm <see cref="DateTime"/> tràn và ném lỗi ở mọi lần đọc giờ.
        /// </summary>
        private static readonly TimeSpan MaximumStoredOffset = TimeSpan.FromDays(3650);

        private readonly ILeagueClock _inner;
        private readonly ILeagueTextStore _store;
        private readonly string _storeKey;
        private TimeSpan _offset;

        /// <summary>Độ lệch chỉ nằm trong RAM: tắt app là về 0.</summary>
        public OffsetLeagueClock(ILeagueClock inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>Nạp độ lệch đã lưu ở <paramref name="storeKey"/>; mỗi lần đặt <see cref="Offset"/> hoặc <see cref="Advance"/> thì ghi lại.</summary>
        public OffsetLeagueClock(ILeagueClock inner, ILeagueTextStore store, string storeKey) : this(inner)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            if (string.IsNullOrEmpty(storeKey)) throw new ArgumentException("Khoá lưu không được rỗng.", nameof(storeKey));
            _storeKey = storeKey;
            _offset = LoadOffset(store, storeKey);
        }

        public TimeSpan Offset
        {
            get => _offset;
            set
            {
                if (value == _offset) return;
                _offset = value;
                SaveOffset();
            }
        }

        public DateTime UtcNow => _inner.UtcNow + _offset;

        public void Advance(TimeSpan duration)
        {
            Offset += duration;
        }

        private void SaveOffset()
        {
            if (_store == null) return;
            if (_offset == TimeSpan.Zero)
            {
                _store.Delete(_storeKey);
                return;
            }
            var record = new LeagueTextRecord(StoreFormat);
            record.SetLong(OffsetTicksKey, _offset.Ticks);
            _store.Write(_storeKey, record.Encode());
        }

        /// <summary>Chuỗi hỏng, khác định dạng hoặc giá trị vô lý → 0 (không ném lỗi lúc khởi động game).</summary>
        private static TimeSpan LoadOffset(ILeagueTextStore store, string storeKey)
        {
            if (!store.TryRead(storeKey, out string text)) return TimeSpan.Zero;
            if (!LeagueTextRecord.TryDecode(text, out LeagueTextRecord record) || record.Format != StoreFormat) return TimeSpan.Zero;
            if (!record.Has(OffsetTicksKey)) return TimeSpan.Zero;

            long ticks = record.GetLong(OffsetTicksKey, 0);
            if (ticks > MaximumStoredOffset.Ticks || ticks < -MaximumStoredOffset.Ticks) return TimeSpan.Zero;
            return TimeSpan.FromTicks(ticks);
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
