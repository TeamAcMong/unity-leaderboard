using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DreamTech.Leaderboard.ViewModel;

namespace DreamTech.Leaderboard.Tests
{
    /// <summary>Dựng board + scene từ Mock với độ trễ 0 (mọi Task hoàn tất đồng bộ nên .Result an toàn trên main thread).</summary>
    internal sealed class LeaderboardScenario
    {
        public const string BoardId = "test-board";

        private LeaderboardScenario(MockLeaderboardService service, ManualScoreSource scoreSource,
                                    InMemoryLeaderboardSnapshotStore snapshotStore, LeaderboardBoard board)
        {
            Service = service;
            ScoreSource = scoreSource;
            SnapshotStore = snapshotStore;
            Board = board;
        }

        public MockLeaderboardService Service { get; }
        public ManualScoreSource ScoreSource { get; }
        public InMemoryLeaderboardSnapshotStore SnapshotStore { get; }
        public LeaderboardBoard Board { get; }

        public static LeaderboardScenario Create(int maximumAnimatedPasses = 18, int topCount = 50, int rowsAbove = 4,
                                                 int rowsBelow = 6, int botCount = 3000, int seed = 7)
        {
            var service = new MockLeaderboardService(new MockLeaderboardOptions
            {
                BotCount = botCount,
                Seed = seed,
                LatencyMilliseconds = 0,
            });
            var scoreSource = new ManualScoreSource("test-score");
            var snapshotStore = new InMemoryLeaderboardSnapshotStore();
            var settings = new LeaderboardBoardSettings(BoardId,
                new FetchWindowSettings(topCount, rowsAbove, rowsBelow, maximumAnimatedPasses), RankTierRule.Default);
            var board = new LeaderboardBoard(settings, service, scoreSource, snapshotStore);
            return new LeaderboardScenario(service, scoreSource, snapshotStore, board);
        }

        /// <summary>Người chơi đã được xem ở startRank (-1 = chưa từng có điểm), sau đó điểm nguồn đạt targetRank.</summary>
        public BoardScene LoadRevealScene(int startRank, int targetRank)
        {
            if (startRank >= 0)
            {
                Service.SetLocalScore(Service.ScoreToReachRank(startRank));
                LeaderboardEntry before = Service.GetLocalEntryAsync(CancellationToken.None).Result;
                Board.MarkRevealed(RankChange.Browse(before));
                ScoreSource.SetScore(before.Score);
            }
            ScoreSource.SetScore(Service.ScoreToReachRank(targetRank));
            return Board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result;
        }

        /// <summary>Lượt CÓ điểm (+<paramref name="gain"/>) mà vẫn đứng hạng <paramref name="rank"/> ⇒ ScoreImproved.</summary>
        public BoardScene LoadScoreImprovedScene(int rank, long gain = 1)
        {
            Service.SetLocalScore(Service.ScoreToReachRank(rank));
            LeaderboardEntry before = Service.GetLocalEntryAsync(CancellationToken.None).Result;
            Board.MarkRevealed(RankChange.Browse(before));
            ScoreSource.SetScore(before.Score + gain);
            return Board.LoadSceneAsync(BoardPresentMode.RevealIfPending, CancellationToken.None).Result;
        }
    }

    /// <summary>Ghi lại mọi nhịp/sự kiện timeline để test đếm.</summary>
    internal sealed class RecordingRevealListener : IRevealListener
    {
        public readonly List<LeaderboardBeat> Beats = new List<LeaderboardBeat>();
        public int LandedCount;
        public int CelebrateCount;
        public int TailRevealedCount;
        public int CameraSnapCount;

        public int CountOf(LeaderboardBeat beat)
        {
            return Beats.Count(recorded => recorded == beat);
        }

        public void OnBeat(LeaderboardBeat beat, in LeaderboardBeatContext context)
        {
            Beats.Add(beat);
        }

        public void OnLanded(RankTier tier, RowState localRow)
        {
            LandedCount++;
        }

        public void OnCelebrate(RankTier tier, RowState localRow)
        {
            CelebrateCount++;
        }

        public void OnTailRevealed(IReadOnlyList<RowState> tail)
        {
            TailRevealedCount++;
        }

        public void OnCameraSnapRequested()
        {
            CameraSnapCount++;
        }
    }

    /// <summary>Backend giả đơn giản để test luồng board mà không phụ thuộc phân bố của Mock.</summary>
    internal sealed class FakeLeaderboardService : ILeaderboardService
    {
        private readonly Dictionary<string, (string Name, long Score)> _players = new Dictionary<string, (string, long)>();

        public FakeLeaderboardService(string localPlayerId = "me", string seasonKey = "season-1")
        {
            LocalPlayerId = localPlayerId;
            SeasonKey = seasonKey;
        }

        public string LocalPlayerId { get; }
        public string SeasonKey { get; set; }
        public int SubmitCallCount { get; private set; }
        public int RangeCallCount { get; private set; }
        public bool FailNextSubmit { get; set; }
        public int LocalEntryCallCount { get; private set; }

        /// <summary>Chạy sau khi đã lấy entry nhưng trước khi trả về — giả lập việc xảy ra lúc chờ mạng (vd đổi mùa). Null = không làm gì.</summary>
        public Action WhileGetLocalEntryInFlight { get; set; }

        public void SetPlayer(string playerId, long score)
        {
            _players[playerId] = (playerId, score);
        }

        public Task<LeaderboardEntry> GetLocalEntryAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LocalEntryCallCount++;
            LeaderboardEntry entry = FindEntry(LocalPlayerId);
            WhileGetLocalEntryInFlight?.Invoke();
            return Task.FromResult(entry);
        }

        public Task<LeaderboardEntry> SubmitScoreAsync(long score, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SubmitCallCount++;
            if (FailNextSubmit)
            {
                FailNextSubmit = false;
                return Task.FromException<LeaderboardEntry>(new InvalidOperationException("submit failed"));
            }
            if (!_players.TryGetValue(LocalPlayerId, out var existing) || score > existing.Score) SetPlayer(LocalPlayerId, score);
            return Task.FromResult(FindEntry(LocalPlayerId));
        }

        public Task<IReadOnlyList<LeaderboardEntry>> GetRangeAsync(int offset, int limit, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RangeCallCount++;
            List<LeaderboardEntry> ranked = Ranked();
            IReadOnlyList<LeaderboardEntry> slice = ranked.Skip(Math.Max(0, offset)).Take(Math.Max(0, limit)).ToList();
            return Task.FromResult(slice);
        }

        private List<LeaderboardEntry> Ranked()
        {
            return _players.OrderByDescending(pair => pair.Value.Score)
                           .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                           .Select((pair, index) => new LeaderboardEntry(pair.Key, pair.Value.Name, pair.Value.Score, index))
                           .ToList();
        }

        private LeaderboardEntry FindEntry(string playerId)
        {
            return Ranked().FirstOrDefault(entry => entry.PlayerId == playerId);
        }
    }
}
