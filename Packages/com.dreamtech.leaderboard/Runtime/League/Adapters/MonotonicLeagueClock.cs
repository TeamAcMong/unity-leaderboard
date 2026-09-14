using System;

namespace DreamTech.Leaderboard.League
{
    /// <summary>
    /// Đồng hồ League không bao giờ lùi: <see cref="UtcNow"/> = max(giờ của đồng hồ bọc trong, mốc cao nhất từng thấy). Mốc được
    /// lưu, nên tắt/mở app mà mất offset cheat, hay người chơi chỉnh giờ máy tới rồi lùi, đều không kéo League về mùa cũ.
    ///
    /// <para>Vì sao cần: mùa của League tính từ giờ. Giờ lùi thì "mùa hiện tại" lùi theo — trước bản 0.2.1 dịch vụ mô phỏng khi
    /// đó bỏ mùa đang giữ, mở lại mùa đã khép (0 cúp) rồi khép lần nữa sinh kết quả trùng. Dịch vụ giờ tự phòng thủ, còn đồng hồ
    /// này chặn từ gốc để mọi phần đọc giờ (đếm ngược, id mùa gắn vào grant, cache) cùng thấy một dòng thời gian chỉ tiến.</para>
    ///
    /// <para>Lắp: <c>new MonotonicLeagueClock(new OffsetLeagueClock(new SystemLeagueClock(), store, "clock.offset"), store,
    /// "clock.highWater")</c>, rồi đưa đồng hồ này cho cả dịch vụ nhóm và <see cref="LeagueSystemBuilder.WithClock"/>. Cheat
    /// "xoá dữ liệu League" phải gọi <see cref="ResetHighWater"/>, không thì League kẹt ở giờ đã tua tới.</para>
    ///
    /// <para>An toàn luồng: trạng thái trong RAM có khoá. Ghi xuống nơi lưu được bọc try/catch — nơi lưu kiểu PlayerPrefs ném lỗi
    /// khi bị gọi ngoài main thread; ghi hỏng thì lần đọc sau thử lại, giờ trả về vẫn đúng.</para>
    /// </summary>
    public sealed class MonotonicLeagueClock : ILeagueClock
    {
        /// <summary>
        /// Mốc chỉ được ghi xuống nơi lưu khi đã vượt bản đã ghi ít nhất chừng này. <see cref="UtcNow"/> bị đọc nhiều lần mỗi khung
        /// hình (mùa hiện tại, thời gian còn lại trên HUD); ghi mỗi lần đọc là gọi <c>PlayerPrefs.Save</c> liên tục. Một phút đủ nhỏ:
        /// mở lại app, League lùi tối đa một phút so với lúc tắt — chỉ đáng kể nếu rơi đúng mốc đổi mùa, và khi đó dịch vụ nhóm vẫn
        /// giữ mùa mới (không mở lại mùa đã khép). Nhảy xa (tua giờ) vượt ngưỡng ngay nên được ghi ở lần đọc kế tiếp.
        /// </summary>
        internal static readonly TimeSpan HighWaterPersistStep = TimeSpan.FromMinutes(1);

        private const int StoreFormat = 1;
        private const string HighWaterTicksKey = "highWater.ticks";

        private readonly object _gate = new object();
        private readonly ILeagueClock _inner;
        private readonly ILeagueTextStore _store;
        private readonly string _storeKey;

        /// <summary><see cref="DateTime.MinValue"/> = chưa thấy mốc nào.</summary>
        private DateTime _highWaterUtc;

        private DateTime _persistedHighWaterUtc;

        public MonotonicLeagueClock(ILeagueClock inner, ILeagueTextStore store, string storeKey)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            if (string.IsNullOrEmpty(storeKey)) throw new ArgumentException("Khoá lưu không được rỗng.", nameof(storeKey));
            _storeKey = storeKey;
            _highWaterUtc = LoadHighWater(store, storeKey);
            _persistedHighWaterUtc = _highWaterUtc;
        }

        /// <summary>max(giờ của đồng hồ bọc trong, mốc cao nhất từng thấy). Không bao giờ nhỏ hơn giá trị đã trả trước đó.</summary>
        public DateTime UtcNow
        {
            get
            {
                DateTime innerUtc = ToUtc(_inner.UtcNow);
                lock (_gate)
                {
                    Observe(innerUtc);
                    return _highWaterUtc;
                }
            }
        }

        /// <summary>Mốc cao nhất từng thấy (không đọc lại đồng hồ bọc trong); <see cref="DateTime.MinValue"/> nếu chưa có.</summary>
        public DateTime HighWaterUtc
        {
            get
            {
                lock (_gate) return _highWaterUtc;
            }
        }

        /// <summary>Đồng hồ bọc trong đang chậm hơn mốc: giờ máy bị chỉnh lùi, hoặc offset cheat bị mất / đặt lại.</summary>
        public bool IsInnerBehind
        {
            get
            {
                DateTime innerUtc = ToUtc(_inner.UtcNow);
                lock (_gate)
                {
                    Observe(innerUtc);
                    return innerUtc < _highWaterUtc;
                }
            }
        }

        /// <summary>Quên mốc và xoá bản lưu — chỉ dành cho cheat xoá dữ liệu League. Lần đọc sau lấy lại giờ của đồng hồ bọc trong.</summary>
        public void ResetHighWater()
        {
            lock (_gate)
            {
                _highWaterUtc = DateTime.MinValue;
                _persistedHighWaterUtc = DateTime.MinValue;
                _store.Delete(_storeKey);
            }
        }

        /// <summary>Gọi trong khoá.</summary>
        private void Observe(DateTime innerUtc)
        {
            if (innerUtc > _highWaterUtc) _highWaterUtc = innerUtc;
            if (_highWaterUtc - _persistedHighWaterUtc < HighWaterPersistStep) return;

            try
            {
                var record = new LeagueTextRecord(StoreFormat);
                record.SetLong(HighWaterTicksKey, _highWaterUtc.Ticks);
                _store.Write(_storeKey, record.Encode());
                _persistedHighWaterUtc = _highWaterUtc;
            }
            catch (Exception)
            {
                // Ghi hỏng (vd PlayerPrefs bị gọi ngoài main thread): giữ mốc đã ghi cũ để lần đọc sau thử lại.
            }
        }

        /// <summary>Chuỗi hỏng, khác định dạng hoặc số ticks ngoài khoảng của <see cref="DateTime"/> → chưa có mốc.</summary>
        private static DateTime LoadHighWater(ILeagueTextStore store, string storeKey)
        {
            if (!store.TryRead(storeKey, out string text)) return DateTime.MinValue;
            if (!LeagueTextRecord.TryDecode(text, out LeagueTextRecord record) || record.Format != StoreFormat) return DateTime.MinValue;

            long ticks = record.GetLong(HighWaterTicksKey, 0);
            if (ticks <= DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) return DateTime.MinValue;
            return new DateTime(ticks, DateTimeKind.Utc);
        }

        private static DateTime ToUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }
    }
}
