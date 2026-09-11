using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DreamTech.Leaderboard
{
    /// <summary>Tham số sinh dữ liệu giả lập.</summary>
    public sealed class MockLeaderboardOptions
    {
        public int BotCount = 2000;
        public int Seed = 7;
        public string LocalPlayerId = "local-player";
        public string LocalDisplayName = "You";
        public long MinScore = 300;
        public long MaxScore = 250000;

        /// <summary>Số mũ phân bố điểm u^k: 3 = top rất thưa, nhiều người điểm thấp, giống bảng thật.</summary>
        public double DistributionExponent = 3.0;

        /// <summary>Độ trễ mạng giả lập mỗi lần gọi (ms). 0 = hoàn tất đồng bộ (dùng cho test).</summary>
        public int LatencyMilliseconds = 200;

        public string SeasonKey = "mock-season";

        public string[] FirstNames =
        {
            "Minh", "Linh", "Huy", "Trang", "Bao", "Khoa", "Vy", "Tuan", "An", "Mai", "Nam", "Thao",
            "Kenji", "Aiko", "Sofia", "Lucas", "Noah", "Emma", "Leo", "Mia", "Kai", "Zoe", "Liam", "Nora",
            "Mateo", "Yuna", "Arjun", "Chloe", "Ivan", "Hana", "Omar", "Lena",
        };

        public string[] NameSuffixes = { "", "", "", "Pro", "_x", "99", "Gamer", "VN", "007", "Star", "King", "Queen" };

        /// <summary>Tên cố định xen vào danh sách bot (tên tiếng Việt, tên rất dài...) để kiểm hiển thị.</summary>
        public string[] FeaturedNames = Array.Empty<string>();

        /// <summary>Cứ mỗi N bot thì một bot nhận tên kế tiếp trong FeaturedNames.</summary>
        public int FeaturedNameEvery = 3;

        public MockLeaderboardOptions Clone()
        {
            var copy = (MockLeaderboardOptions)MemberwiseClone();
            copy.FirstNames = (string[])FirstNames.Clone();
            copy.NameSuffixes = (string[])NameSuffixes.Clone();
            copy.FeaturedNames = FeaturedNames != null ? (string[])FeaturedNames.Clone() : Array.Empty<string>();
            return copy;
        }
    }

    /// <summary>Lỗi giả lập do <see cref="MockLeaderboardService.FailNextCall"/>.</summary>
    public sealed class MockLeaderboardException : Exception
    {
        public MockLeaderboardException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Backend giả lập chạy hoàn toàn trong RAM để dựng và tinh chỉnh animation trước khi có server.
    ///
    /// <para>Hoà điểm được phân định ổn định: người đạt điểm trước đứng trên (bot sinh trước "đạt trước", người chơi luôn đạt sau
    /// cùng). Bản tham khảo dùng List.Sort không ổn định nên thứ tự hoà điểm có thể khác nhau giữa Mono và IL2CPP — với chỉ số
    /// kiểu level (rất nhiều người cùng điểm) điều đó làm hạng nhảy lung tung.</para>
    /// </summary>
    public sealed class MockLeaderboardService : ILeaderboardService
    {
        private readonly MockLeaderboardOptions _options;
        private readonly List<MockPlayer> _bots = new List<MockPlayer>();
        private readonly List<MockPlayer> _ranked = new List<MockPlayer>();
        private MockPlayer _local;
        private int _localRank = -1;
        private bool _failNextCall;

        public MockLeaderboardService(MockLeaderboardOptions options)
        {
            _options = (options ?? new MockLeaderboardOptions()).Clone();
            LatencyMilliseconds = _options.LatencyMilliseconds;
            GenerateBots();
            Rebuild();
        }

        public string LocalPlayerId => _options.LocalPlayerId;
        public string SeasonKey => _options.SeasonKey;

        public int LatencyMilliseconds { get; set; }

        public int TotalCount => _ranked.Count;
        public int BotCount => _bots.Count;
        public bool HasLocalEntry => _local != null;

        /// <summary>Hạng 0-based hiện tại của người chơi; -1 nếu chưa có điểm.</summary>
        public int LocalRank => _local != null ? _localRank : -1;

        public long LocalScore => _local != null ? _local.Score : 0;

        // ---------------------------------------------------------------- ILeaderboardService

        public async Task<LeaderboardEntry> GetLocalEntryAsync(CancellationToken cancellationToken)
        {
            await DelayAsync(cancellationToken);
            ThrowIfFailing();
            return _local != null ? ToEntry(_local, _localRank) : null;
        }

        public async Task<LeaderboardEntry> SubmitScoreAsync(long score, CancellationToken cancellationToken)
        {
            await DelayAsync(cancellationToken);
            ThrowIfFailing();
            if (_local == null) _local = CreateLocal(score);
            else if (score > _local.Score) _local.Score = score;
            Rebuild();
            return ToEntry(_local, _localRank);
        }

        public async Task<IReadOnlyList<LeaderboardEntry>> GetRangeAsync(int offset, int limit, CancellationToken cancellationToken)
        {
            await DelayAsync(cancellationToken);
            ThrowIfFailing();
            int start = Math.Max(0, offset);
            int end = Math.Min(_ranked.Count, Math.Max(0, offset) + Math.Max(0, limit));
            var result = new List<LeaderboardEntry>(Math.Max(0, end - start));
            for (int index = start; index < end; index++) result.Add(ToEntry(_ranked[index], index));
            return result;
        }

        // ---------------------------------------------------------------- Công cụ debug / test

        /// <summary>Đặt điểm trực tiếp, bỏ qua luật giữ điểm tốt nhất. Dùng để dựng trạng thái ban đầu.</summary>
        public void SetLocalScore(long score)
        {
            if (_local == null) _local = CreateLocal(score);
            else _local.Score = score;
            Rebuild();
        }

        /// <summary>Xoá điểm người chơi (giả lập người chơi mới).</summary>
        public void ClearLocalEntry()
        {
            _local = null;
            Rebuild();
        }

        /// <summary>Điểm cần đạt để đứng ở hạng targetRank (0-based). Gần đúng khi nhiều bot trùng điểm.</summary>
        public long ScoreToReachRank(int targetRank)
        {
            if (_bots.Count == 0) return 1;
            if (targetRank >= _bots.Count) return Math.Max(0L, _bots[_bots.Count - 1].Score - 1);
            if (targetRank < 0) targetRank = 0;
            return _bots[targetRank].Score + 1;
        }

        /// <summary>Lần gọi backend kế tiếp sẽ ném <see cref="MockLeaderboardException"/> (kiểm trạng thái lỗi + Retry).</summary>
        public void FailNextCall()
        {
            _failNextCall = true;
        }

        // ---------------------------------------------------------------- Nội bộ

        private void GenerateBots()
        {
            var random = new Random(_options.Seed);
            string[] firstNames = _options.FirstNames != null && _options.FirstNames.Length > 0 ? _options.FirstNames : new[] { "Player" };
            string[] suffixes = _options.NameSuffixes != null && _options.NameSuffixes.Length > 0 ? _options.NameSuffixes : new[] { "" };
            string[] featured = _options.FeaturedNames ?? Array.Empty<string>();
            int featuredEvery = Math.Max(1, _options.FeaturedNameEvery);
            long scoreSpan = Math.Max(0, _options.MaxScore - _options.MinScore);
            int featuredCursor = 0;

            for (int index = 0; index < _options.BotCount; index++)
            {
                double uniform = random.NextDouble();
                long score = _options.MinScore + (long)(scoreSpan * Math.Pow(uniform, _options.DistributionExponent));
                string name = firstNames[random.Next(firstNames.Length)] + suffixes[random.Next(suffixes.Length)];
                if (featured.Length > 0 && index % featuredEvery == 0)
                {
                    name = featured[featuredCursor % featured.Length];
                    featuredCursor++;
                }
                _bots.Add(new MockPlayer("bot-" + index, name, score, index));
            }
            _bots.Sort(CompareBots);
        }

        private static int CompareBots(MockPlayer first, MockPlayer second)
        {
            if (first.Score != second.Score) return second.Score.CompareTo(first.Score);
            return first.ReachedOrder.CompareTo(second.ReachedOrder);
        }

        private MockPlayer CreateLocal(long score)
        {
            // Người chơi luôn "đạt sau cùng": hoà điểm thì đứng sau bot.
            return new MockPlayer(_options.LocalPlayerId, _options.LocalDisplayName, score, int.MaxValue);
        }

        private void Rebuild()
        {
            _ranked.Clear();
            _localRank = -1;
            bool localInserted = _local == null;
            for (int index = 0; index < _bots.Count; index++)
            {
                if (!localInserted && _local.Score > _bots[index].Score)
                {
                    _localRank = _ranked.Count;
                    _ranked.Add(_local);
                    localInserted = true;
                }
                _ranked.Add(_bots[index]);
            }
            if (!localInserted)
            {
                _localRank = _ranked.Count;
                _ranked.Add(_local);
            }
        }

        private Task DelayAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL player không có thread pool: Task.Delay không đáng tin, bỏ qua độ trễ.
            return Task.CompletedTask;
#else
            return LatencyMilliseconds > 0 ? Task.Delay(LatencyMilliseconds, cancellationToken) : Task.CompletedTask;
#endif
        }

        private void ThrowIfFailing()
        {
            if (!_failNextCall) return;
            _failNextCall = false;
            throw new MockLeaderboardException("Lỗi giả lập từ MockLeaderboardService.FailNextCall().");
        }

        private static LeaderboardEntry ToEntry(MockPlayer player, int rank)
        {
            return new LeaderboardEntry(player.PlayerId, player.DisplayName, player.Score, rank);
        }

        private sealed class MockPlayer
        {
            public MockPlayer(string playerId, string displayName, long score, int reachedOrder)
            {
                PlayerId = playerId;
                DisplayName = displayName;
                Score = score;
                ReachedOrder = reachedOrder;
            }

            public string PlayerId { get; }
            public string DisplayName { get; }
            public long Score { get; set; }

            /// <summary>Thứ tự đạt điểm: nhỏ hơn = đạt trước = đứng trên khi hoà điểm.</summary>
            public int ReachedOrder { get; }
        }
    }
}
