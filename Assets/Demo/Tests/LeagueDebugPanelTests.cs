using System;
using System.Collections;
using System.Collections.Generic;
using DreamTech.Leaderboard.League;
using DreamTech.Leaderboard.League.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DreamTech.Leaderboard.Demo.Tests
{
    /// <summary>
    /// Nút tua giờ của <see cref="LeagueDebugPanel"/> khi lắp giống game thật: đồng hồ không-lùi bọc đồng hồ tua được. QA tua lùi
    /// (hoặc chỉnh giờ máy) làm giờ máy + offset chậm hơn mốc; lúc đó các nút tua vẫn phải đưa giờ League tới đúng chỗ.
    /// </summary>
    public class LeagueDebugPanelTests
    {
        private static readonly DateTime SeasonAnchorUtc = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan SeasonLength = TimeSpan.FromDays(7);
        private static readonly TimeSpan StartIntoSeason = TimeSpan.FromDays(3);
        private static readonly TimeSpan ClockSetBack = TimeSpan.FromHours(48);
        private static readonly TimeSpan LeagueStep = TimeSpan.FromHours(1);
        private const int TrophiesPerWin = 10;
        private const int ServiceLatencyMilliseconds = 100;
        private const float TimeoutSeconds = 5f;
        private const string ReloadErrorLogPrefix = "Lỗi tải: ";

        private LeagueDebugPanel _panel;
        private LeagueSystem _league;
        private OffsetLeagueClock _offsetClock;
        private MonotonicLeagueClock _monotonicClock;

        [SetUp]
        public void SetUp()
        {
            var store = new InMemoryLeagueTextStore();
            _offsetClock = new OffsetLeagueClock(new ManualLeagueClock(SeasonAnchorUtc + StartIntoSeason), store, "clock.offset");
            _monotonicClock = new MonotonicLeagueClock(_offsetClock, store, "clock.high-water");
            var rules = new LeagueRules(new LeagueLadder(new[]
            {
                new LeagueTierDefinition("bronze", 5, 5),
                new LeagueTierDefinition("silver", 5, 5),
            }));
            var simulation = new SimulatedLeagueGroupService(new SimulatedLeagueOptions { LatencyMilliseconds = 0 }, rules, _monotonicClock, store);
            _league = new LeagueSystemBuilder("panel-test", rules, new WinStreakLadder(new[] { new WinStreakStep(1) }))
                      .WithGroupService(simulation)
                      .WithSchedule(new FixedLengthSeasonSchedule(SeasonAnchorUtc, SeasonLength))
                      .WithClock(_monotonicClock)
                      .WithTextStore(store)
                      .WithTrophyRule(new MultipliedTrophyRule(new[] { TrophiesPerWin }))
                      .Build();
            _panel = LeagueDebugPanel.Create(_league, simulation, _offsetClock, _monotonicClock, null, keepAcrossScenes: false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_panel != null) UnityEngine.Object.Destroy(_panel.gameObject);
        }

        [Test]
        public void AdvanceToSeasonEnd_WhileMachineClockBehindHighWater_ReachesNextSeason()
        {
            string seasonBefore = _league.CurrentSeason.SeasonId;
            _offsetClock.Advance(-ClockSetBack);
            Assume.That(_monotonicClock.IsInnerBehind, Is.True);

            _panel.AdvanceToSeasonEnd();

            Assert.AreNotEqual(seasonBefore, _league.CurrentSeason.SeasonId, "Tua đúng 'thời gian còn lại' từ mốc thì vẫn kẹt trong mùa cũ");
        }

        [Test]
        public void AdvanceLeagueTime_WhileMachineClockBehindHighWater_MovesLeagueTimeByExactStep()
        {
            DateTime leagueTimeBefore = _league.Clock.UtcNow;
            _offsetClock.Advance(-ClockSetBack);

            _panel.AdvanceLeagueTime(LeagueStep);

            Assert.AreEqual(leagueTimeBefore + LeagueStep, _league.Clock.UtcNow);
            Assert.IsFalse(_monotonicClock.IsInnerBehind);
        }

        /// <summary>
        /// "Xoá dữ liệu" trong lúc vòng tải lại đang chờ dịch vụ: lượt gọi cũ bị dịch vụ bỏ bằng lỗi tạm (không còn là
        /// OperationCanceledException). Vòng tải lại phải ghi lỗi một dòng rồi chạy tiếp vòng đã xếp hàng, kết thúc với trang mới —
        /// không kẹt ở "đang gọi", không để exception lọt khỏi async void.
        /// </summary>
        [UnityTest]
        public IEnumerator ResetEverything_WhileReloadWaitsOnService_ReloadFinishesWithFreshPage()
        {
            var logged = new List<string>();
            _panel.Logged += logged.Add;
            _panel.Simulation.LatencyMilliseconds = ServiceLatencyMilliseconds;
            _panel.Reload();
            Assume.That(_panel.IsBusy, Is.True, "Lượt tải phải đang chờ độ trễ của dịch vụ");

            _panel.ResetEverything();
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (_panel.IsBusy)
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail("Vòng tải lại không kết thúc sau " + TimeoutSeconds + "s");
                yield return null;
            }

            Assert.IsNotNull(_panel.Page);
            Assert.AreEqual(_league.CurrentSeason.SeasonId, _panel.Page.Season.SeasonId);
            Assert.IsTrue(logged.Exists(line => line.StartsWith(ReloadErrorLogPrefix, StringComparison.Ordinal)),
                          "Lượt gọi cũ bị bỏ phải được ghi là lỗi tải, không bị nuốt như huỷ");
        }
    }
}
