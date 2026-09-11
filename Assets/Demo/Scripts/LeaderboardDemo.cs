using System.Threading.Tasks;
using DreamTech.Leaderboard.UI;
using UnityEngine;

namespace DreamTech.Leaderboard.Demo
{
    /// <summary>
    /// Composition root + bảng điều khiển của scene demo (chỉ nằm trong dev project, không đi theo package).
    ///
    /// <para>Lắp Mock backend + nơi lưu snapshot trong RAM thành một board, không gắn <see cref="IScoreSource"/> nên board chỉ đọc
    /// và các nút thao tác thẳng vào Mock. Chọn host để thấy cùng một widget chạy ở màn riêng, popup hay khối trong màn Win.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LeaderboardDemo : MonoBehaviour
    {
        private const float SlowMotionTimeScale = 0.1f;
        private const float ReferenceScreenHeight = 1920f;

        [SerializeField] private LeaderboardDemoHost[] hosts = new LeaderboardDemoHost[0];
        [Tooltip("Để trống = tuỳ chọn Mock mặc định.")]
        [SerializeField] private MockLeaderboardConfig mockConfig;
        [Tooltip("Để trống = tham số board mặc định.")]
        [SerializeField] private LeaderboardBoardConfig boardConfig;
        [SerializeField, Min(1)] private int startingRank = 121;
        [Tooltip("Chiều cao dải nút điều khiển ở đáy màn, theo hệ 1920. Host được dựng chừa đúng khoảng này.")]
        [SerializeField, Min(0f)] private float controlsHeight = 360f;

        private MockLeaderboardService _mock;
        private LeaderboardBoard _board;
        private int _hostIndex;
        private bool _isSlowMotion;
        private string _lastResult = "-";

        public LeaderboardBoard Board => _board;
        public MockLeaderboardService Mock => _mock;
        public LeaderboardDemoHost CurrentHost => hosts.Length > 0 ? hosts[_hostIndex] : null;
        public int HostCount => hosts.Length;

        private void Awake()
        {
            _mock = new MockLeaderboardService(mockConfig != null ? mockConfig.CreateOptions() : new MockLeaderboardOptions());
            LeaderboardBoardSettings settings = boardConfig != null
                ? boardConfig.CreateSettings()
                : new LeaderboardBoardSettings("demo", FetchWindowSettings.Default, RankTierRule.Default);
            _board = new LeaderboardBoard(settings, _mock, null, new InMemoryLeaderboardSnapshotStore());
            LeaderboardBoardRegistry.Register(_board);
            SetRank(startingRank);
        }

        private void OnDestroy()
        {
            if (_board != null) LeaderboardBoardRegistry.Unregister(_board);
        }

        // ---------------------------------------------------------------- Thao tác (nút + test PlayMode dùng chung)

        public void SelectHost(int index)
        {
            if (hosts.Length == 0) return;
            index = Mathf.Clamp(index, 0, hosts.Length - 1);
            if (index == _hostIndex) return;
            CurrentHost.Hide();
            _hostIndex = index;
        }

        public Task<LeaderboardPresentResult> Open()
        {
            return Present(BoardPresentMode.Browse);
        }

        public Task<LeaderboardPresentResult> Reveal()
        {
            return Present(BoardPresentMode.RevealIfPending);
        }

        /// <summary>Đặt hạng (1-based) và coi như đã xem — không diễn.</summary>
        public void SetRank(int rank)
        {
            _mock.SetLocalScore(_mock.ScoreToReachRank(Mathf.Max(0, rank - 1)));
            MarkCurrentAsRevealed();
        }

        public Task<LeaderboardPresentResult> Climb(int ranks)
        {
            MarkCurrentAsRevealed();
            int currentRank = _mock.LocalRank < 0 ? _mock.BotCount : _mock.LocalRank;
            _mock.SetLocalScore(_mock.ScoreToReachRank(Mathf.Max(0, currentRank - Mathf.Max(0, ranks))));
            return Reveal();
        }

        /// <summary>Leo tới hạng (1-based). Nhiều bot hoà điểm ở vùng top có thể làm vượt quá hạng đích một chút.</summary>
        public Task<LeaderboardPresentResult> ReachRank(int rank)
        {
            MarkCurrentAsRevealed();
            _mock.SetLocalScore(_mock.ScoreToReachRank(Mathf.Max(0, rank - 1)));
            return Reveal();
        }

        /// <summary>Tăng điểm mà giữ hạng (pill BEST). Không có khoảng trống điểm ngay trên thì giữ nguyên điểm.</summary>
        public Task<LeaderboardPresentResult> ImproveScoreOnly()
        {
            MarkCurrentAsRevealed();
            long score = _mock.LocalScore;
            int rank = _mock.LocalRank;
            _mock.SetLocalScore(score + 1);
            if (_mock.LocalRank != rank) _mock.SetLocalScore(score);
            return Reveal();
        }

        public Task<LeaderboardPresentResult> NoChange()
        {
            MarkCurrentAsRevealed();
            return Reveal();
        }

        public Task<LeaderboardPresentResult> NewPlayer()
        {
            _board.ClearRevealedSnapshot();
            _mock.SetLocalScore(_mock.ScoreToReachRank(Random.Range(30, 200)));
            return Reveal();
        }

        public void FailNextCall()
        {
            _mock.FailNextCall();
        }

        public void Skip()
        {
            if (CurrentHost != null) CurrentHost.Widget.Skip();
        }

        public void Close()
        {
            if (CurrentHost != null) CurrentHost.Hide();
        }

        public void SetSlowMotion(bool isSlowMotion)
        {
            _isSlowMotion = isSlowMotion;
            for (int index = 0; index < hosts.Length; index++)
            {
                if (hosts[index] != null) hosts[index].Widget.DebugTimeScale = isSlowMotion ? SlowMotionTimeScale : 1f;
            }
        }

        // ---------------------------------------------------------------- Nội bộ

        private Task<LeaderboardPresentResult> Present(BoardPresentMode mode)
        {
            LeaderboardDemoHost host = CurrentHost;
            if (host == null) return Task.FromResult(default(LeaderboardPresentResult));
            host.Widget.DebugTimeScale = _isSlowMotion ? SlowMotionTimeScale : 1f;
            Task<LeaderboardPresentResult> present = host.Show(_board, mode);
            TrackResultAsync(present);
            return present;
        }

        private async void TrackResultAsync(Task<LeaderboardPresentResult> present)
        {
            LeaderboardPresentResult result = await present;
            _lastResult = result.Outcome + " " + result.Change.Kind;
        }

        private void MarkCurrentAsRevealed()
        {
            if (!_mock.HasLocalEntry) return;
            _board.MarkRevealed(RankChange.Create(RankChangeKind.Unchanged, _mock.LocalRank, _mock.LocalRank, _mock.LocalScore, _mock.LocalScore));
        }

        private void OnGUI()
        {
            float scale = Screen.height / ReferenceScreenHeight;
            float bandHeight = controlsHeight * scale;
            var band = new Rect(0f, Screen.height - bandHeight, Screen.width, bandHeight);
            float rowHeight = bandHeight / 4f;
            GUI.skin.button.fontSize = Mathf.RoundToInt(rowHeight * 0.32f);
            GUI.skin.label.fontSize = Mathf.RoundToInt(rowHeight * 0.3f);

            GUILayout.BeginArea(band);
            GUILayout.BeginHorizontal();
            for (int index = 0; index < hosts.Length; index++)
            {
                bool isSelected = GUILayout.Toggle(index == _hostIndex, hosts[index].DisplayName, GUI.skin.button, GUILayout.Height(rowHeight));
                if (isSelected && index != _hostIndex) SelectHost(index);
            }
            if (GUILayout.Button("Close", GUILayout.Height(rowHeight))) Close();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Open", GUILayout.Height(rowHeight))) Open();
            if (GUILayout.Button("+3", GUILayout.Height(rowHeight))) Climb(3);
            if (GUILayout.Button("+12", GUILayout.Height(rowHeight))) Climb(12);
            if (GUILayout.Button("+300", GUILayout.Height(rowHeight))) Climb(300);
            if (GUILayout.Button("Top 3", GUILayout.Height(rowHeight))) ReachRank(3);
            if (GUILayout.Button("#1", GUILayout.Height(rowHeight))) ReachRank(1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("New", GUILayout.Height(rowHeight))) NewPlayer();
            if (GUILayout.Button("Best", GUILayout.Height(rowHeight))) ImproveScoreOnly();
            if (GUILayout.Button("Same", GUILayout.Height(rowHeight))) NoChange();
            if (GUILayout.Button("Fail next", GUILayout.Height(rowHeight))) FailNextCall();
            bool slowMotion = GUILayout.Toggle(_isSlowMotion, "Slow x0.1", GUI.skin.button, GUILayout.Height(rowHeight));
            if (slowMotion != _isSlowMotion) SetSlowMotion(slowMotion);
            GUILayout.EndHorizontal();

            GUILayout.Label("Rank #" + (_mock.LocalRank + 1) + "   Score " + _mock.LocalScore + "   Last: " + _lastResult);
            GUILayout.EndArea();
        }
    }
}
