using System;
using NUnit.Framework;

namespace DreamTech.Leaderboard.League.Tests
{
    /// <summary>
    /// Đồng hồ của League: offset cheat phải sống qua lần mở lại app, và giờ League không bao giờ lùi. Hai lỗi này chính là gốc của
    /// playtest C2 (tắt/mở Play → offset về 0 → mùa lùi → mở lại mùa đã khép).
    /// </summary>
    [TestFixture]
    public class LeagueClockTests
    {
        private const string OffsetKey = "clock.offset";
        private const string HighWaterKey = "clock.highWater";

        private static readonly DateTime Start = LeagueTestFactory.Anchor + TimeSpan.FromDays(1);

        // ---------------------------------------------------------------- OffsetLeagueClock

        [Test]
        public void OffsetClock_WithoutStore_OffsetLivesInMemoryOnly()
        {
            var inner = new ManualLeagueClock(Start);
            var clock = new OffsetLeagueClock(inner);
            clock.Advance(TimeSpan.FromHours(3));

            Assert.AreEqual(Start + TimeSpan.FromHours(3), clock.UtcNow);
            Assert.AreEqual(TimeSpan.Zero, new OffsetLeagueClock(inner).Offset);
        }

        [Test]
        public void OffsetClock_WithStore_RestoresOffsetOnNewInstance()
        {
            var store = new InMemoryLeagueTextStore();
            var inner = new ManualLeagueClock(Start);
            var firstRun = new OffsetLeagueClock(inner, store, OffsetKey);
            firstRun.Advance(TimeSpan.FromDays(7));
            firstRun.Advance(TimeSpan.FromHours(-1));

            var reopened = new OffsetLeagueClock(inner, store, OffsetKey);
            Assert.AreEqual(TimeSpan.FromDays(7) - TimeSpan.FromHours(1), reopened.Offset);
            Assert.AreEqual(Start + reopened.Offset, reopened.UtcNow);

            reopened.Offset = TimeSpan.Zero;
            Assert.IsFalse(store.TryRead(OffsetKey, out _), "Offset về 0 thì xoá bản lưu");
            Assert.AreEqual(TimeSpan.Zero, new OffsetLeagueClock(inner, store, OffsetKey).Offset);
        }

        [TestCase("not a record")]
        [TestCase("format=1\noffset.ticks=abc\n")]
        [TestCase("format=9\noffset.ticks=36000000000\n")]
        [TestCase("format=1\noffset.ticks=9223372036854775807\n")]
        public void OffsetClock_CorruptStoredValue_StartsAtZero(string storedText)
        {
            var store = new InMemoryLeagueTextStore();
            store.Write(OffsetKey, storedText);
            var clock = new OffsetLeagueClock(new ManualLeagueClock(Start), store, OffsetKey);

            Assert.AreEqual(TimeSpan.Zero, clock.Offset);
            Assert.AreEqual(Start, clock.UtcNow);
        }

        // ---------------------------------------------------------------- MonotonicLeagueClock

        [Test]
        public void MonotonicClock_InnerGoesBack_UtcNowHoldsHighWater()
        {
            var inner = new ManualLeagueClock(Start);
            var clock = new MonotonicLeagueClock(inner, new InMemoryLeagueTextStore(), HighWaterKey);
            Assert.AreEqual(Start, clock.UtcNow);
            Assert.IsFalse(clock.IsInnerBehind);

            inner.Set(Start - TimeSpan.FromHours(5));
            Assert.AreEqual(Start, clock.UtcNow);
            Assert.IsTrue(clock.IsInnerBehind);

            inner.Set(Start + TimeSpan.FromHours(2));
            Assert.AreEqual(Start + TimeSpan.FromHours(2), clock.UtcNow);
            Assert.AreEqual(Start + TimeSpan.FromHours(2), clock.HighWaterUtc);
            Assert.IsFalse(clock.IsInnerBehind);
            Assert.AreEqual(DateTimeKind.Utc, clock.UtcNow.Kind);
        }

        [Test]
        public void MonotonicClock_NewInstanceOnSameStore_KeepsHighWater()
        {
            var store = new InMemoryLeagueTextStore();
            var inner = new ManualLeagueClock(Start + TimeSpan.FromDays(9));
            _ = new MonotonicLeagueClock(inner, store, HighWaterKey).UtcNow;

            inner.Set(Start);
            var reopened = new MonotonicLeagueClock(inner, store, HighWaterKey);

            Assert.AreEqual(Start + TimeSpan.FromDays(9), reopened.UtcNow);
            Assert.IsTrue(reopened.IsInnerBehind);
        }

        [Test]
        public void MonotonicClock_WritesStoreOnlyWhenHighWaterMovesByPersistStep()
        {
            var store = new CountingLeagueTextStore();
            var inner = new ManualLeagueClock(Start);
            var clock = new MonotonicLeagueClock(inner, store, HighWaterKey);

            _ = clock.UtcNow;
            Assert.AreEqual(1, store.WriteCount, "Mốc đầu tiên được ghi ngay");

            TimeSpan belowStep = TimeSpan.FromTicks(MonotonicLeagueClock.HighWaterPersistStep.Ticks / 4);
            for (int read = 1; read < 4; read++)
            {
                inner.Set(Start + TimeSpan.FromTicks(belowStep.Ticks * read));
                _ = clock.UtcNow;
                _ = clock.UtcNow;
                _ = clock.IsInnerBehind;
            }
            Assert.AreEqual(1, store.WriteCount, "Đọc giờ liên tục không được ghi nơi lưu mỗi lần");

            inner.Set(Start + MonotonicLeagueClock.HighWaterPersistStep);
            _ = clock.UtcNow;
            Assert.AreEqual(2, store.WriteCount);

            inner.Set(Start + TimeSpan.FromDays(7));
            _ = clock.UtcNow;
            Assert.AreEqual(3, store.WriteCount, "Tua giờ xa được ghi ngay ở lần đọc kế tiếp");

            inner.Set(Start);
            _ = clock.UtcNow;
            Assert.AreEqual(3, store.WriteCount, "Đồng hồ bọc trong lùi thì không có gì để ghi");
        }

        [TestCase("garbage")]
        [TestCase("format=1\nhighWater.ticks=-5\n")]
        [TestCase("format=1\nhighWater.ticks=99999999999999999999\n")]
        [TestCase("format=4\nhighWater.ticks=638000000000000000\n")]
        public void MonotonicClock_CorruptStoredValue_IsIgnored(string storedText)
        {
            var store = new InMemoryLeagueTextStore();
            store.Write(HighWaterKey, storedText);
            var clock = new MonotonicLeagueClock(new ManualLeagueClock(Start), store, HighWaterKey);

            Assert.AreEqual(Start, clock.UtcNow);
        }

        [Test]
        public void MonotonicClock_ResetHighWater_FollowsInnerAgain_AndClearsStore()
        {
            var store = new InMemoryLeagueTextStore();
            var inner = new ManualLeagueClock(Start + TimeSpan.FromDays(30));
            var clock = new MonotonicLeagueClock(inner, store, HighWaterKey);
            _ = clock.UtcNow;
            inner.Set(Start);

            clock.ResetHighWater();
            Assert.IsFalse(store.TryRead(HighWaterKey, out _));
            Assert.AreEqual(DateTime.MinValue, clock.HighWaterUtc);
            Assert.AreEqual(Start, clock.UtcNow);

            inner.Set(Start - TimeSpan.FromDays(1));
            Assert.AreEqual(Start, new MonotonicLeagueClock(inner, store, HighWaterKey).UtcNow, "Mốc mới được lưu lại sau khi xoá");
        }

        /// <summary>Playtest C2 ở tầng đồng hồ: offset cheat kiểu 0.2.0 (không lưu) mất khi mở lại app, mùa League vẫn không lùi.</summary>
        [Test]
        public void PlaytestReplay_CheatOffsetLostOnRestart_LeagueSeasonDoesNotGoBack()
        {
            FixedLengthSeasonSchedule schedule = LeagueTestFactory.CreateSchedule();
            var store = new InMemoryLeagueTextStore();
            var deviceClock = new ManualLeagueClock(Start);

            var cheatOffset = new OffsetLeagueClock(deviceClock);
            var leagueClock = new MonotonicLeagueClock(cheatOffset, store, HighWaterKey);
            SeasonWindow seasonBeforeCheat = schedule.GetSeasonAt(leagueClock.UtcNow);
            cheatOffset.Advance(seasonBeforeCheat.TimeLeft(leagueClock.UtcNow) + TimeSpan.FromMinutes(1));
            SeasonWindow seasonAfterCheat = schedule.GetSeasonAt(leagueClock.UtcNow);
            Assume.That(seasonAfterCheat.SeasonId, Is.Not.EqualTo(seasonBeforeCheat.SeasonId));

            var reopenedOffset = new OffsetLeagueClock(deviceClock);
            var reopenedLeagueClock = new MonotonicLeagueClock(reopenedOffset, store, HighWaterKey);

            Assert.AreEqual(seasonAfterCheat.SeasonId, schedule.GetSeasonAt(reopenedLeagueClock.UtcNow).SeasonId);
            Assert.IsTrue(reopenedLeagueClock.IsInnerBehind);
        }

        [Test]
        public void PersistedOffsetUnderMonotonicClock_RestartKeepsSeasonWithoutRelyingOnHighWater()
        {
            FixedLengthSeasonSchedule schedule = LeagueTestFactory.CreateSchedule();
            var store = new InMemoryLeagueTextStore();
            var deviceClock = new ManualLeagueClock(Start);
            var cheatOffset = new OffsetLeagueClock(deviceClock, store, OffsetKey);
            var leagueClock = new MonotonicLeagueClock(cheatOffset, store, HighWaterKey);
            cheatOffset.Advance(LeagueTestFactory.SeasonLength);
            SeasonWindow cheatedSeason = schedule.GetSeasonAt(leagueClock.UtcNow);

            var reopenedLeagueClock = new MonotonicLeagueClock(new OffsetLeagueClock(deviceClock, store, OffsetKey), store, HighWaterKey);

            Assert.AreEqual(cheatedSeason.SeasonId, schedule.GetSeasonAt(reopenedLeagueClock.UtcNow).SeasonId);
            Assert.IsFalse(reopenedLeagueClock.IsInnerBehind, "Offset đã lưu thì đồng hồ bọc trong không bị chậm");
        }
    }
}
