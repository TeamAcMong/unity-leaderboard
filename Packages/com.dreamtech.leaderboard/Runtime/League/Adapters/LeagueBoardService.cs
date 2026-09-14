using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    ///
    /// <para><b>Chỉ dùng trên main thread, không <c>ConfigureAwait(false)</c></b> (cùng lý do với <see cref="LeagueSystem"/>). Lượt
    /// tải gửi hàng chờ cúp (ghi <see cref="ILeagueTextStore"/>, bắn <c>StateChanged</c>), gọi dịch vụ nhóm, đọc đồng hồ của host rồi
    /// ghi snapshot. Trước đây các await ở đây bỏ context: trên main thread của Unity, phần tiếp theo bị đẩy sang thread pool nên lượt
    /// hỏi bảng sau khi gửi cúp (cùng <c>Save</c> của dịch vụ mô phỏng trong lượt đó), lượt tải sau <see cref="SubmitScoreAsync"/>, việc
    /// đọc đồng hồ và lưu mốc của <see cref="MonotonicLeagueClock"/> chạy ngoài main thread — PlayerPrefs ném lỗi, lượt mở trang thất bại.</para>
    /// </summary>
    public sealed class LeagueBoardService : ILeaderboardService, IScoreSource, IDisposable
    {
        /// <summary>
        /// Thời gian coi snapshot còn "tươi". <see cref="LeaderboardBoard.LoadSceneAsync"/> hỏi top, cửa sổ quanh người chơi và
        /// entry của người chơi gần như cùng lúc; cache ngắn này gộp chúng thành một lượt gọi mạng thay vì ba.
        ///
        /// <para>Đo bằng HAI đồng hồ, snapshot chỉ tươi khi cả hai cùng nói tươi:</para>
        /// <list type="bullet">
        /// <item>thời gian thực đơn điệu (<see cref="Stopwatch"/>, lưu mốc lúc ghi snapshot) trôi chưa tới ngưỡng này — không theo giờ
        /// máy, không theo cheat. Chỉ đo bằng giờ League thì <see cref="MonotonicLeagueClock"/> đứng yên ở mốc khi giờ máy chậm hơn mốc
        /// (giờ máy bị chỉnh lùi sau khi tua), hiệu giờ = 0 mãi và snapshot "tươi" suốt hàng giờ: mở trang bao nhiêu lần cũng thấy bảng
        /// cũ, không gọi dịch vụ;</item>
        /// <item>giờ League tính từ lúc ghi snapshot nằm trong [0, ngưỡng) — cheat tua giờ tới hoặc lùi đều làm cache hết hạn ngay.</item>
        /// </list>
        /// <see cref="Invalidate"/> xoá cả hai mốc.
        /// </summary>
        internal static readonly TimeSpan SnapshotFreshness = TimeSpan.FromSeconds(0.5);

        /// <summary>Số tick của <see cref="TimeSpan"/> ứng với một tick của <see cref="Stopwatch"/> (tần số Stopwatch tuỳ nền tảng).</summary>
        private static readonly double TimeSpanTicksPerStopwatchTick = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;

        private readonly LeagueSystem _league;
        private readonly object _gate = new object();

        /// <summary>Nguồn thời gian thực đơn điệu tính từ một gốc bất kỳ. Mặc định là <see cref="Stopwatch"/>; test cắm nguồn tay.</summary>
        private readonly Func<TimeSpan> _readRealTime;

        /// <summary>
        /// Lượt tải đang bay gần nhất — đối tượng lượt, KHÔNG phải Task gắn token của một người gọi — cùng mùa nó hỏi và số thứ tự của nó
        /// (ba field đổi cùng nhau trong khoá). Chỉ người hỏi CÙNG mùa được nhập vào, và chỉ khi lượt chưa đóng; lượt đang huỷ / đã xong
        /// thì người sau mở lượt mới. Không ai gán lại null: lượt đã đóng tự bị bỏ qua, nên lượt cũ xong muộn không thể xoá lượt đang bay
        /// mới hơn.
        /// </summary>
        private SharedOperationRun<LeagueGroupSnapshot> _inFlight;
        private SeasonWindow _inFlightSeason;
        private long _inFlightRunNumber;

        /// <summary>Số thứ tự của lượt tải mở gần nhất. Lượt mở sau có số lớn hơn.</summary>
        private long _lastRunNumber;

        /// <summary>Số thứ tự của lượt đã ghi <see cref="_snapshot"/>. Lượt mở trước nó mà xong muộn thì không ghi đè.</summary>
        private long _snapshotRunNumber;

        private LeagueGroupSnapshot _snapshot;

        /// <summary>Giờ League lúc ghi snapshot; <see cref="DateTime.MinValue"/> sau <see cref="Invalidate"/>.</summary>
        private DateTime _snapshotUtc = DateTime.MinValue;

        /// <summary>Thời gian thực (theo <see cref="_readRealTime"/>) lúc ghi snapshot; null = chưa ghi, hoặc đã <see cref="Invalidate"/>.</summary>
        private TimeSpan? _snapshotRealTime;

        public LeagueBoardService(LeagueSystem league) : this(league, ReadStopwatchTime)
        {
        }

        /// <summary>Cho test cắm nguồn thời gian thực để kiểm độ tươi của snapshot mà không phải ngủ.</summary>
        internal LeagueBoardService(LeagueSystem league, Func<TimeSpan> readRealTime)
        {
            _league = league ?? throw new ArgumentNullException(nameof(league));
            _readRealTime = readRealTime ?? throw new ArgumentNullException(nameof(readRealTime));
            _league.StateChanged += OnLeagueStateChanged;
        }

        public string LocalPlayerId => _league.GroupService.LocalPlayerId;

        /// <summary>Mùa hiện tại. Sang mùa mới là khoá đổi, nên snapshot "lần xem trước" của bảng tự hết hiệu lực.</summary>
        public string SeasonKey => _league.CurrentSeason.SeasonId;

        // ------------------------------------------------------------------ IScoreSource

        public string MetricId => "league-trophies";

        public event Action ScoreChanged;

        /// <summary>
        /// Điểm "đúng ra phải có" = cúp dịch vụ đang giữ + cúp vừa thắng chưa gửi được CỦA CÙNG MÙA với bảng. Bằng nhau thì
        /// <see cref="LeaderboardBoard"/> bỏ qua, khác nhau thì nó gọi <see cref="SubmitScoreAsync"/> để đẩy hàng chờ. Cúp còn nợ
        /// của mùa khác không cộng vào: nó thuộc kết quả của mùa đó, không phải điểm trên bảng này. Snapshot gần nhất thuộc mùa khác
        /// mùa hiện tại (vừa qua mốc đổi mùa, chưa tải lại) thì trả false: điểm của mùa cũ không phải điểm của bảng đang hỏi.
        /// </summary>
        public bool TryGetScore(out long score)
        {
            LeagueGroupSnapshot snapshot = _snapshot;
            if (snapshot == null || snapshot.LocalRank < 0 || !snapshot.Season.Equals(_league.CurrentSeason))
            {
                score = 0;
                return false;
            }

            score = snapshot.LocalTrophies + _league.GetUnsentTrophies(snapshot.Season.SeasonId);
            return true;
        }

        // ------------------------------------------------------------------ ILeaderboardService

        public async Task<LeaderboardEntry> GetLocalEntryAsync(CancellationToken cancellationToken)
        {
            LeagueGroupSnapshot snapshot = await GetSnapshotAsync(cancellationToken);
            return snapshot.LocalEntry;
        }

        /// <summary>
        /// Đẩy hàng chờ cúp lên dịch vụ rồi trả entry mới. <paramref name="score"/> bị bỏ qua có chủ đích: League không cho
        /// client đặt điểm tuyệt đối (xem ghi chú ở đầu lớp).
        ///
        /// <para>Entry trả về phải có các grant vừa gửi, nên sau khi gửi xong KHÔNG nhập vào lượt tải cùng mùa đã mở TRƯỚC lúc đó: lượt
        /// ấy có thể đã hỏi bảng trước khi grant tới dịch vụ (vd HUD mở lượt tải, người chơi thắng, <see cref="LeaderboardBoard"/> thấy
        /// điểm lệch và gửi điểm). Mở lượt mới thay vì trả bảng cũ rồi để lần sync sau phát hiện lệch lần nữa.</para>
        /// </summary>
        public async Task<LeaderboardEntry> SubmitScoreAsync(long score, CancellationToken cancellationToken)
        {
            await _league.FlushPendingTrophiesAsync(cancellationToken);
            long lastRunOpenedBeforeFlushCompleted = InvalidateAndReadLastRunNumber();
            LeagueGroupSnapshot snapshot = await GetSnapshotAsync(cancellationToken, lastRunOpenedBeforeFlushCompleted);
            return snapshot.LocalEntry;
        }

        public async Task<IReadOnlyList<LeaderboardEntry>> GetRangeAsync(int offset, int limit, CancellationToken cancellationToken)
        {
            if (offset < 0) offset = 0;
            if (limit <= 0) return Array.Empty<LeaderboardEntry>();

            LeagueGroupSnapshot snapshot = await GetSnapshotAsync(cancellationToken);
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
            InvalidateAndReadLastRunNumber();
        }

        /// <summary>
        /// <see cref="Invalidate"/> rồi trả số thứ tự của lượt tải mở gần nhất, đọc trong CÙNG khoá: mọi lượt có số không lớn hơn số này
        /// đã mở trước mốc xoá cache.
        /// </summary>
        private long InvalidateAndReadLastRunNumber()
        {
            lock (_gate)
            {
                _snapshotUtc = DateTime.MinValue;
                _snapshotRealTime = null;
                return _lastRunNumber;
            }
        }

        /// <summary>
        /// Snapshot của mùa hiện tại: cache còn tươi → dùng luôn; đang có lượt tải CÙNG mùa chưa đóng → nhập vào; còn lại mở lượt mới.
        /// Lượt đang bay của mùa khác KHÔNG được dùng lại: qua mốc đổi mùa mà nhận entry của mùa cũ thì
        /// <see cref="LeaderboardBoard.LoadSceneAsync"/> ghép entry mùa cũ với khoá mùa mới → lần mở sau diễn RankUp / RankDown giả.
        ///
        /// <para>Lượt tải chạy bằng token riêng (<see cref="SharedOperationRun{TResult}"/>): <paramref name="cancellationToken"/> huỷ thì
        /// chỉ người gọi này thôi chờ; lượt chỉ dừng (huỷ lượt gọi dịch vụ đang bay) khi mọi người đang chờ đều đã huỷ. Trước đây lượt
        /// chạy bằng token của người mở nên HUD bị tắt làm popup đang nhập cùng lượt nhận huỷ không phải của mình → widget hiện lỗi.</para>
        /// </summary>
        /// <param name="onlyJoinRunsOpenedAfter">
        /// Chỉ nhập vào lượt đang bay có số thứ tự LỚN HƠN số này (mở sau mốc đó). 0 = lượt nào cùng mùa cũng được.
        /// <see cref="SubmitScoreAsync"/> truyền số của lượt mở gần nhất lúc gửi cúp xong để không nhận bảng hỏi trước khi grant tới.
        /// </param>
        private Task<LeagueGroupSnapshot> GetSnapshotAsync(CancellationToken cancellationToken, long onlyJoinRunsOpenedAfter = 0)
        {
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<LeagueGroupSnapshot>(cancellationToken);

            SeasonWindow season = _league.CurrentSeason;
            DateTime nowUtc = _league.Clock.UtcNow;
            TimeSpan realTimeNow = _readRealTime();
            SharedOperationRun<LeagueGroupSnapshot> run;
            long openedRunNumber = 0;

            lock (_gate)
            {
                bool isFresh = _snapshot != null && _snapshot.Season.Equals(season) && IsSnapshotWithinFreshness(nowUtc, realTimeNow);
                if (isFresh) return Task.FromResult(_snapshot);

                run = _inFlight;
                bool joined = run != null &&
                              season.Equals(_inFlightSeason) &&
                              _inFlightRunNumber > onlyJoinRunsOpenedAfter &&
                              run.TryJoin();
                if (!joined)
                {
                    run = new SharedOperationRun<LeagueGroupSnapshot>();
                    run.TryJoin();
                    openedRunNumber = ++_lastRunNumber;
                    _inFlight = run;
                    _inFlightSeason = season;
                    _inFlightRunNumber = openedRunNumber;
                }
            }

            // Chạy lượt mới NGOÀI khoá: lượt gửi cúp (bắn StateChanged cho code của host) và gọi dịch vụ.
            if (openedRunNumber > 0) _ = run.RunAsync(runToken => LoadAsync(season, openedRunNumber, runToken));
            return run.WaitAsync(cancellationToken);
        }

        /// <summary>Lõi của một lượt tải, chạy bằng token riêng của lượt.</summary>
        private async Task<LeagueGroupSnapshot> LoadAsync(SeasonWindow season, long runNumber, CancellationToken runToken)
        {
            // Đẩy hàng chờ cúp TRƯỚC khi hỏi bảng (best-effort, lỗi mạng thì vẫn tải bảng). Hỏi bảng mùa mới trong khi còn cúp
            // mùa cũ chưa gửi là để dịch vụ khép mùa cũ thiếu cúp — đúng đường mở trang League ngay sau khi hết mùa.
            if (_league.PendingTrophyGrantCount > 0) await _league.TryFlushPendingTrophiesAsync(runToken);
            LeagueGroupSnapshot snapshot = await _league.GroupService.GetGroupAsync(season, runToken);
            if (snapshot == null) throw new InvalidOperationException("GetGroupAsync trả về null — dịch vụ nhóm phải ném lỗi thay vì trả null.");

            lock (_gate)
            {
                // Lượt cũ xong muộn không được ghi đè snapshot mới hơn: chỉ ghi khi chưa lượt nào mở sau nó đã ghi, VÀ snapshot vừa tải
                // không thuộc mùa cũ hơn snapshot đang giữ (trừ khi nó là mùa hiện tại).
                bool isNewerRun = runNumber > _snapshotRunNumber;
                bool isCurrentSeason = snapshot.Season.Equals(_league.CurrentSeason);
                bool isNotOlderSeason = _snapshot == null || isCurrentSeason || snapshot.Season.StartUtc >= _snapshot.Season.StartUtc;
                if (isNewerRun && isNotOlderSeason)
                {
                    _snapshot = snapshot;
                    _snapshotUtc = _league.Clock.UtcNow;
                    _snapshotRealTime = _readRealTime();
                    _snapshotRunNumber = runNumber;
                }
            }
            return snapshot;
        }

        /// <summary>
        /// Gọi trong khoá. Tươi khi đã có mốc ghi snapshot, thời gian thực trôi từ mốc nằm trong [0, <see cref="SnapshotFreshness"/>) VÀ
        /// giờ League trôi từ mốc cũng nằm trong [0, <see cref="SnapshotFreshness"/>) (xem ghi chú ở <see cref="SnapshotFreshness"/>).
        /// </summary>
        private bool IsSnapshotWithinFreshness(DateTime nowUtc, TimeSpan realTimeNow)
        {
            if (!_snapshotRealTime.HasValue) return false;

            TimeSpan realTimeElapsed = realTimeNow - _snapshotRealTime.Value;
            if (realTimeElapsed < TimeSpan.Zero || realTimeElapsed >= SnapshotFreshness) return false;

            TimeSpan leagueTimeElapsed = nowUtc - _snapshotUtc;
            return leagueTimeElapsed >= TimeSpan.Zero && leagueTimeElapsed < SnapshotFreshness;
        }

        /// <summary>Thời gian thực đơn điệu từ <see cref="Stopwatch"/> (gốc tuỳ nền tảng): không theo giờ máy, không theo cheat tua giờ.</summary>
        private static TimeSpan ReadStopwatchTime()
        {
            return TimeSpan.FromTicks((long)(Stopwatch.GetTimestamp() * TimeSpanTicksPerStopwatchTick));
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
