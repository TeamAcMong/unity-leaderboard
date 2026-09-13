using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DreamTech.Leaderboard.League
{
    /// <summary>
    /// Cắm một League vào bộ hiển thị của leaderboard: nhóm mùa này trở thành một bảng xếp hạng bình thường, nên
    /// <see cref="LeaderboardBoard"/>, list ảo hoá, <c>RankUpPlanner</c> và <c>RevealTimeline</c> chạy được y như mọi bảng khác
    /// mà không cần biết League là gì.
    ///
    /// <para>Đây là miếng Lego nối hai module: đổi <see cref="ILeagueGroupService"/> sang backend thật thì phần hiển thị
    /// không đổi một dòng; ngược lại, muốn dùng UI khác cho League thì bỏ lớp này ra là xong.</para>
    ///
    /// <para>Khác biệt duy nhất so với một leaderboard thường: <b>điểm do dịch vụ giữ, không do client gửi</b>. Cúp chỉ tăng
    /// qua <see cref="LeagueTrophyGrant"/> (idempotent theo grant id) nên <see cref="SubmitScoreAsync"/> KHÔNG gửi con số
    /// tuyệt đối — nó đẩy nốt hàng chờ cúp rồi trả về entry sau khi đẩy. Nhờ vậy không có đường nào cho client tự đặt điểm.</para>
    /// </summary>
    public sealed class LeagueBoardService : ILeaderboardService, IScoreSource, IDisposable
    {
        /// <summary>
        /// Thời gian coi snapshot còn "tươi". <see cref="LeaderboardBoard.LoadSceneAsync"/> hỏi top, cửa sổ quanh người chơi và
        /// entry của người chơi gần như cùng lúc; cache ngắn này gộp chúng thành một lượt gọi mạng thay vì ba.
        /// Đo bằng đồng hồ của League nên cheat tua giờ cũng làm cache hết hạn ngay.
        /// </summary>
        private static readonly TimeSpan SnapshotFreshness = TimeSpan.FromSeconds(0.5);

        private readonly LeagueSystem _league;
        private readonly object _gate = new object();

        private Task<LeagueGroupSnapshot> _inFlight;
        private LeagueGroupSnapshot _snapshot;
        private DateTime _snapshotUtc = DateTime.MinValue;

        public LeagueBoardService(LeagueSystem league)
        {
            _league = league ?? throw new ArgumentNullException(nameof(league));
            _league.StateChanged += OnLeagueStateChanged;
        }

        public string LocalPlayerId => _league.GroupService.LocalPlayerId;

        /// <summary>Mùa hiện tại. Sang mùa mới là khoá đổi, nên snapshot "lần xem trước" của bảng tự hết hiệu lực.</summary>
        public string SeasonKey => _league.CurrentSeason.SeasonId;

        // ------------------------------------------------------------------ IScoreSource

        public string MetricId => "league-trophies";

        public event Action ScoreChanged;

        /// <summary>
        /// Điểm "đúng ra phải có" = cúp dịch vụ đang giữ + cúp vừa thắng chưa gửi được. Bằng nhau thì
        /// <see cref="LeaderboardBoard"/> bỏ qua, khác nhau thì nó gọi <see cref="SubmitScoreAsync"/> để đẩy hàng chờ.
        /// </summary>
        public bool TryGetScore(out long score)
        {
            LeagueGroupSnapshot snapshot = _snapshot;
            if (snapshot == null || snapshot.LocalRank < 0)
            {
                score = 0;
                return false;
            }

            score = snapshot.LocalTrophies + _league.UnsentTrophies;
            return true;
        }

        // ------------------------------------------------------------------ ILeaderboardService

        public async Task<LeaderboardEntry> GetLocalEntryAsync(CancellationToken cancellationToken)
        {
            LeagueGroupSnapshot snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            return snapshot.LocalEntry;
        }

        /// <summary>
        /// Đẩy hàng chờ cúp lên dịch vụ rồi trả entry mới. <paramref name="score"/> bị bỏ qua có chủ đích: League không cho
        /// client đặt điểm tuyệt đối (xem ghi chú ở đầu lớp).
        /// </summary>
        public async Task<LeaderboardEntry> SubmitScoreAsync(long score, CancellationToken cancellationToken)
        {
            await _league.FlushPendingTrophiesAsync(cancellationToken).ConfigureAwait(false);
            Invalidate();
            LeagueGroupSnapshot snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            return snapshot.LocalEntry;
        }

        public async Task<IReadOnlyList<LeaderboardEntry>> GetRangeAsync(int offset, int limit, CancellationToken cancellationToken)
        {
            if (offset < 0) offset = 0;
            if (limit <= 0) return Array.Empty<LeaderboardEntry>();

            LeagueGroupSnapshot snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<LeaderboardEntry> standings = snapshot.Standings;
            if (offset >= standings.Count) return Array.Empty<LeaderboardEntry>();

            int count = Math.Min(limit, standings.Count - offset);
            var range = new LeaderboardEntry[count];
            for (int index = 0; index < count; index++) range[index] = standings[offset + index];
            return range;
        }

        // ------------------------------------------------------------------ Snapshot

        /// <summary>Bảng của nhóm ở lần lấy gần nhất; null khi chưa lấy lần nào. Dùng cho zone band và HUD của trang League.</summary>
        public LeagueGroupSnapshot LastKnownSnapshot => _snapshot;

        /// <summary>Buộc lượt hỏi sau phải gọi lại dịch vụ (sau khi cộng cúp, đổi mùa, hoặc cheat sửa dữ liệu).</summary>
        public void Invalidate()
        {
            lock (_gate) _snapshotUtc = DateTime.MinValue;
        }

        private Task<LeagueGroupSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            SeasonWindow season = _league.CurrentSeason;
            DateTime nowUtc = _league.Clock.UtcNow;

            lock (_gate)
            {
                bool isFresh = _snapshot != null &&
                               _snapshot.Season.Equals(season) &&
                               nowUtc - _snapshotUtc < SnapshotFreshness &&
                               nowUtc >= _snapshotUtc;
                if (isFresh) return Task.FromResult(_snapshot);
                if (_inFlight != null && !_inFlight.IsCompleted) return _inFlight;

                _inFlight = FetchAsync(season, cancellationToken);
                return _inFlight;
            }
        }

        private async Task<LeagueGroupSnapshot> FetchAsync(SeasonWindow season, CancellationToken cancellationToken)
        {
            LeagueGroupSnapshot snapshot = await _league.GroupService.GetGroupAsync(season, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _snapshot = snapshot;
                _snapshotUtc = _league.Clock.UtcNow;
                _inFlight = null;
            }
            return snapshot;
        }

        private void OnLeagueStateChanged()
        {
            // Trạng thái đổi (thắng level, đẩy cúp, khép mùa...) thì bảng cũ không còn đáng tin — lượt hỏi sau phải gọi dịch vụ.
            Invalidate();

            // Thắng level xong là số cúp "đúng ra phải có" đã khác — báo để nơi lắp ráp quyết định sync ngay hay đợi mở trang.
            Action handler = ScoreChanged;
            if (handler != null) handler();
        }

        public void Dispose()
        {
            _league.StateChanged -= OnLeagueStateChanged;
        }
    }
}
