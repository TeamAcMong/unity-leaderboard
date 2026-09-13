using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DreamTech.Leaderboard.League.Tests
{
    /// <summary>
    /// <see cref="LeagueBoardService"/> là chỗ nối League với bộ hiển thị leaderboard. Nếu nó sai thì trang League vẫn hiện
    /// nhưng không bao giờ diễn lên hạng — đúng lỗi "toàn là tĩnh" — nên test đi hết đường qua <see cref="LeaderboardBoard"/>.
    /// </summary>
    [TestFixture]
    public class LeagueBoardServiceTests
    {
        private static readonly LevelWinContext NormalWin = new LevelWinContext(12, 0);

        [Test]
        public void GetRange_ReturnsStandingsSlice_AndSeasonKeyIsSeasonId()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            var service = new LeagueBoardService(scenario.System);

            IReadOnlyList<LeaderboardEntry> range = service.GetRangeAsync(5, 4, CancellationToken.None).Result;
            LeaderboardEntry local = service.GetLocalEntryAsync(CancellationToken.None).Result;

            Assert.AreEqual(4, range.Count);
            for (int index = 0; index < range.Count; index++) Assert.AreEqual(5 + index, range[index].Rank);
            Assert.IsNotNull(local);
            Assert.AreEqual(scenario.System.CurrentSeason.SeasonId, service.SeasonKey);
            Assert.AreEqual(scenario.Simulation.LocalPlayerId, service.LocalPlayerId);
        }

        [Test]
        public void GetRange_PastEndOrEmptyLimit_ReturnsEmpty()
        {
            var service = new LeagueBoardService(LeagueScenario.Create().System);

            Assert.AreEqual(0, service.GetRangeAsync(1000, 10, CancellationToken.None).Result.Count);
            Assert.AreEqual(0, service.GetRangeAsync(0, 0, CancellationToken.None).Result.Count);
            Assert.AreEqual(3, service.GetRangeAsync(27, 10, CancellationToken.None).Result.Count, "Nhóm 30 người, từ hạng 27 còn 3");
        }

        [Test]
        public void ConcurrentQueries_HitGroupServiceOnce()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            var service = new LeagueBoardService(scenario.System);

            // Đúng kiểu LoadSceneAsync hỏi: entry của mình + top + cửa sổ, gần như cùng lúc.
            Task.WhenAll(service.GetLocalEntryAsync(CancellationToken.None),
                         service.GetRangeAsync(0, 10, CancellationToken.None),
                         service.GetRangeAsync(10, 10, CancellationToken.None)).Wait();

            Assert.AreEqual(1, scenario.Service.GetGroupCallCount);
        }

        [Test]
        public void TryGetScore_IsServiceTrophiesPlusUnsent()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            var service = new LeagueBoardService(scenario.System);
            Assert.IsFalse(service.TryGetScore(out _), "Chưa tải bảng thì chưa biết điểm");

            LeaderboardEntry before = service.GetLocalEntryAsync(CancellationToken.None).Result;
            scenario.System.RecordLevelWin(NormalWin);

            Assert.IsTrue(service.TryGetScore(out long score));
            Assert.AreEqual(before.Score + 10, score);
        }

        [Test]
        public void SubmitScore_IgnoresValue_FlushesQueue_ReturnsFreshEntry()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            var service = new LeagueBoardService(scenario.System);
            LeaderboardEntry before = service.GetLocalEntryAsync(CancellationToken.None).Result;
            scenario.System.RecordLevelWin(NormalWin);
            scenario.System.RecordLevelWin(NormalWin);

            // Con số truyền vào vô nghĩa: League không cho client tự đặt điểm.
            LeaderboardEntry after = service.SubmitScoreAsync(999999, CancellationToken.None).Result;

            Assert.AreEqual(before.Score + 20, after.Score);
            Assert.AreEqual(0, scenario.System.UnsentTrophies);
            Assert.AreEqual(2, scenario.Service.AddTrophiesCallCount);
        }

        [Test]
        public void LeagueStateChange_RaisesScoreChanged()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            var service = new LeagueBoardService(scenario.System);
            int raised = 0;
            service.ScoreChanged += () => raised++;

            scenario.System.RecordLevelWin(NormalWin);
            service.Dispose();
            scenario.System.RecordLevelWin(NormalWin);

            Assert.AreEqual(1, raised, "Dispose phải gỡ đăng ký");
        }

        [Test]
        public void SeasonRoll_ChangesSeasonKey_AndRefetches()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            var service = new LeagueBoardService(scenario.System);
            _ = service.GetLocalEntryAsync(CancellationToken.None).Result;
            string firstKey = service.SeasonKey;

            scenario.AdvancePastSeasonEnd();
            _ = service.GetLocalEntryAsync(CancellationToken.None).Result;

            Assert.AreNotEqual(firstKey, service.SeasonKey);
            Assert.AreEqual(2, scenario.Service.GetGroupCallCount, "Snapshot mùa cũ không được dùng cho mùa mới");
        }

        [Test]
        public void ThroughLeaderboardBoard_WinsAfterLastView_RevealAsRankUp()
        {
            LeagueScenario scenario = LeagueScenario.Create();
            var service = new LeagueBoardService(scenario.System);
            var board = new LeaderboardBoard(new LeaderboardBoardSettings("league", new FetchWindowSettings(50, 4, 6, 15), new RankTierRule(3)),
                                             service, service, new InMemoryLeaderboardSnapshotStore());

            // Lần mở đầu mùa: xem xong thì ghi nhận là đã xem.
            BoardScene first = board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result;
            board.MarkRevealed(first.Change);
            int rankBefore = first.Change.ToRank;
            Assume.That(rankBefore, Is.GreaterThan(0), "Seed phải để người chơi không đứng nhất sẵn");

            // Thắng liền mấy màn (streak nhân cúp) cho chắc chắn vượt ít nhất một người.
            for (int index = 0; index < 12; index++) scenario.System.RecordLevelWin(NormalWin);
            Assert.IsTrue(board.HasUnrevealedChange, "Có cúp chưa gửi thì phải báo có thay đổi chưa xem");

            BoardScene second = board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result;

            Assert.AreEqual(RankChangeKind.RankUp, second.Change.Kind);
            Assert.AreEqual(rankBefore, second.Change.FromRank);
            Assert.Less(second.Change.ToRank, rankBefore);
            Assert.AreEqual(0, scenario.System.UnsentTrophies, "Mở trang là đẩy hết cúp chờ");
            Assert.AreEqual(second.Change.ToRank, second.Rows[second.LocalRowIndex].Entry.Rank);
        }
    }
}
