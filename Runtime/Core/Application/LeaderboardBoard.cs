using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DreamTech.Leaderboard
{
    /// <summary>Tham số lắp ráp của một board.</summary>
    public sealed class LeaderboardBoardSettings
    {
        public LeaderboardBoardSettings(string boardId, FetchWindowSettings fetch, RankTierRule tierRule)
        {
            if (string.IsNullOrEmpty(boardId)) throw new ArgumentException("Board id không được rỗng.", nameof(boardId));
            BoardId = boardId;
            Fetch = fetch;
            TierRule = tierRule;
        }

        public string BoardId { get; }
        public FetchWindowSettings Fetch { get; }
        public RankTierRule TierRule { get; }
    }

    /// <summary>
    /// Board mặc định: ghép backend + nguồn điểm + nơi lưu snapshot.
    ///
    /// <para>Board tự kéo điểm từ <see cref="IScoreSource"/>; nơi gọi không truyền điểm nên không bị buộc vào chỉ số cụ thể.
    /// Chỉ submit khi điểm nguồn tốt hơn điểm trên backend, và luôn đọc lại backend trước khi so, nên lần submit lỗi
    /// sẽ tự được thử lại ở lần sync sau mà không cần hàng đợi riêng.</para>
    /// </summary>
    public sealed class LeaderboardBoard : ILeaderboardBoard
    {
        private static readonly IReadOnlyList<LeaderboardEntry> EmptyEntries = Array.Empty<LeaderboardEntry>();

        /// <summary>
        /// Số lượt sync tối đa của một lần tải khi khoá mùa cứ đổi trong lúc sync. Mốc đổi mùa rơi đúng lúc tải là hiếm; đổi liên tiếp
        /// nhiều lần chỉ có khi đồng hồ bị tua trong lúc tải — khi đó dừng lại với khoá của lượt sync cuối (vẫn khớp entry).
        /// </summary>
        internal const int MaximumSyncAttemptsWhileSeasonChanges = 3;

        private readonly LeaderboardBoardSettings _settings;
        private readonly ILeaderboardService _service;
        private readonly IScoreSource _scoreSource;
        private readonly ILeaderboardSnapshotStore _snapshotStore;

        private LeaderboardEntry _lastKnownLocalEntry;
        private RevealLease _activeLease;

        /// <param name="scoreSource">Có thể null: board khi đó không bao giờ tự submit, chỉ đọc.</param>
        public LeaderboardBoard(LeaderboardBoardSettings settings, ILeaderboardService service, IScoreSource scoreSource,
                                ILeaderboardSnapshotStore snapshotStore)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
            _scoreSource = scoreSource;
        }

        public string BoardId => _settings.BoardId;
        public RankTierRule TierRule => _settings.TierRule;
        public FetchWindowSettings FetchSettings => _settings.Fetch;

        /// <summary>Chỉ dành cho công cụ debug/cheat (vd thao tác trực tiếp Mock).</summary>
        public ILeaderboardService Service => _service;

        public IScoreSource ScoreSource => _scoreSource;

        /// <summary>Entry người chơi biết được ở lần sync gần nhất; null nếu chưa sync hoặc chưa có dữ liệu.</summary>
        public LeaderboardEntry LastKnownLocalEntry => _lastKnownLocalEntry;

        public bool HasUnrevealedChange
        {
            get
            {
                if (_scoreSource != null && _scoreSource.TryGetScore(out long sourceScore))
                {
                    long knownScore = _lastKnownLocalEntry != null ? _lastKnownLocalEntry.Score : long.MinValue;
                    if (sourceScore > knownScore) return true;
                }
                if (_lastKnownLocalEntry == null) return false;

                RankChange change = RankChange.Resolve(LoadSnapshot(), _lastKnownLocalEntry, _service.SeasonKey);
                return IsWorthRevealing(change.Kind);
            }
        }

        public async Task<LeaderboardEntry> SyncScoreAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LeaderboardEntry entry = await _service.GetLocalEntryAsync(cancellationToken);

            if (_scoreSource != null && _scoreSource.TryGetScore(out long score) && (entry == null || score > entry.Score))
            {
                entry = await _service.SubmitScoreAsync(score, cancellationToken);
                if (entry == null) throw new InvalidOperationException("SubmitScoreAsync trả về null — backend phải trả entry sau khi submit.");
            }

            _lastKnownLocalEntry = entry;
            return entry;
        }

        /// <summary>
        /// Sync rồi tải đúng đoạn dữ liệu cần. Khoá mùa đi kèm thay đổi (<see cref="RankChange.SeasonKey"/>) là khoá đọc NGAY TRƯỚC
        /// lượt sync sinh ra entry của người chơi — không đọc sau sync: mốc đổi mùa rơi vào lúc đang tải (độ trễ mạng) thì entry
        /// thuộc mùa cũ còn khoá đọc sau đã là mùa mới, snapshot ghi (hạng mùa cũ, khoá mùa mới) và lần mở sau diễn RankUp / RankDown
        /// giả thay vì NEW. Khoá đổi trong lúc sync thì sync lại (tối đa <see cref="MaximumSyncAttemptsWhileSeasonChanges"/> lượt) để
        /// entry và khoá cùng thuộc mùa mới.
        /// </summary>
        public async Task<BoardScene> LoadSceneAsync(BoardPresentMode mode, CancellationToken cancellationToken)
        {
            string seasonKey = _service.SeasonKey;
            LeaderboardEntry local = await SyncScoreAsync(cancellationToken);
            for (int attempt = 1; attempt < MaximumSyncAttemptsWhileSeasonChanges; attempt++)
            {
                string seasonKeyAfterSync = _service.SeasonKey;
                if (string.Equals(seasonKeyAfterSync, seasonKey, StringComparison.Ordinal)) break;
                seasonKey = seasonKeyAfterSync;
                local = await SyncScoreAsync(cancellationToken);
            }

            RankChange change = mode == BoardPresentMode.RevealIfPending
                ? RankChange.Resolve(LoadSnapshot(), local, seasonKey)
                : RankChange.Browse(local, seasonKey);
            FetchPlan plan = FetchWindowPlanner.Plan(change, _settings.Fetch);

            // Top và đoạn quanh người chơi tải song song.
            Task<IReadOnlyList<LeaderboardEntry>> topTask = FetchAsync(plan.Top, cancellationToken);
            Task<IReadOnlyList<LeaderboardEntry>> windowTask = FetchAsync(plan.Window, cancellationToken);
            await Task.WhenAll(topTask, windowTask);
            cancellationToken.ThrowIfCancellationRequested();

            List<BoardRow> rows = BoardRowsBuilder.Build(topTask.Result, windowTask.Result, local, _service.LocalPlayerId);
            int localIndex = BoardRowsBuilder.IndexOfLocal(rows);
            if (localIndex < 0) change = RankChange.NoLocalEntry;

            return new BoardScene(BoardId, rows, localIndex, change, _settings.TierRule, plan, mode);
        }

        /// <summary>
        /// Ghi snapshot "đã xem" theo mùa của bảng đã tải (<see cref="RankChange.SeasonKey"/>), không theo mùa lúc gọi: màn diễn
        /// dài vài giây, mùa đổi giữa chừng mà ghi theo mùa mới thì lần mở sau so hạng mùa cũ với bảng mùa mới và diễn RankUp /
        /// RankDown giả thay vì NEW. Thay đổi tạo không kèm mùa (<see cref="RankChange.Create"/>) thì dùng mùa hiện tại.
        /// </summary>
        public void MarkRevealed(in RankChange change)
        {
            if (!change.HasLocalEntry) return;
            string seasonKey = change.SeasonKey ?? _service.SeasonKey;
            _snapshotStore.Save(BoardId, new RevealSnapshot(change.ToRank, change.ToScore, seasonKey));
        }

        /// <summary>Quên lần xem cuối: lần mở sau sẽ diễn lại như người chơi mới. Dành cho cheat/debug.</summary>
        public void ClearRevealedSnapshot()
        {
            _snapshotStore.Clear(BoardId);
        }

        public bool TryBeginReveal(out IDisposable lease)
        {
            if (_activeLease != null)
            {
                lease = null;
                return false;
            }
            _activeLease = new RevealLease(this);
            lease = _activeLease;
            return true;
        }

        public static bool IsWorthRevealing(RankChangeKind kind)
        {
            return kind == RankChangeKind.NewEntry || kind == RankChangeKind.RankUp || kind == RankChangeKind.ScoreImproved;
        }

        private RevealSnapshot? LoadSnapshot()
        {
            return _snapshotStore.TryLoad(BoardId, out RevealSnapshot snapshot) ? snapshot : (RevealSnapshot?)null;
        }

        private Task<IReadOnlyList<LeaderboardEntry>> FetchAsync(RankRange range, CancellationToken cancellationToken)
        {
            return range.IsEmpty
                ? Task.FromResult(EmptyEntries)
                : _service.GetRangeAsync(range.Offset, range.Count, cancellationToken);
        }

        private sealed class RevealLease : IDisposable
        {
            private LeaderboardBoard _owner;

            public RevealLease(LeaderboardBoard owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                if (_owner == null) return;
                if (_owner._activeLease == this) _owner._activeLease = null;
                _owner = null;
            }
        }
    }
}
