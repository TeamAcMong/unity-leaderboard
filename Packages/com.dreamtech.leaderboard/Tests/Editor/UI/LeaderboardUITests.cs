using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamTech.Leaderboard.EditorTools;
using DreamTech.Leaderboard.ViewModel;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DreamTech.Leaderboard.UI.Tests
{
    [TestFixture]
    public class PackagePrefabContractTests
    {
        [Test]
        public void RowPrefab_PassesContract()
        {
            var row = AssetDatabase.LoadAssetAtPath<GameObject>(LeaderboardEditorPaths.RowPrefabPath);
            Assert.IsNotNull(row, "Thiếu prefab row — chạy Tools/DreamTech/Leaderboard/Create Default Assets");
            CollectionAssert.IsEmpty(LeaderboardPrefabValidator.ValidateRow(row.GetComponent<LeaderboardEntryView>()));
        }

        [Test]
        public void WidgetPrefab_PassesContract()
        {
            var widget = AssetDatabase.LoadAssetAtPath<GameObject>(LeaderboardEditorPaths.WidgetPrefabPath);
            Assert.IsNotNull(widget);
            CollectionAssert.IsEmpty(LeaderboardPrefabValidator.ValidateWidget(widget.GetComponent<LeaderboardWidget>(), new MotionSettings().LiftScale));
        }

        [Test]
        public void PackagePrefabs_DependOnlyOnPackageTmpUguiAndBuiltIns()
        {
            string[] allowed =
            {
                LeaderboardEditorPaths.PackageRoot + "/",
                "Packages/com.unity.textmeshpro/",
                "Packages/com.unity.ugui/",
                "Assets/TextMesh Pro/",
                "Resources/unity_builtin_extra",
                "Library/unity default resources",
            };
            CollectionAssert.IsEmpty(LeaderboardPrefabValidator.ValidateDependencies(LeaderboardEditorPaths.WidgetPrefabPath, allowed));
            CollectionAssert.IsEmpty(LeaderboardPrefabValidator.ValidateDependencies(LeaderboardEditorPaths.RowPrefabPath, allowed));
        }

        [Test]
        public void PlaceholderArt_IsGeneratedByCurrentGeneratorVersion()
        {
            Assert.IsTrue(PlaceholderArtGenerator.IsCurrentVersion(LeaderboardEditorPaths.ArtFolder, out string staleAsset), staleAsset);
        }
    }

    [TestFixture]
    public class EntryViewInitialLetterTests
    {
        [TestCase("Nam", "N")]
        [TestCase("vy", "V")]
        [TestCase("_x", "X")]
        [TestCase("<color=red>Hacker</color>", "C")]
        [TestCase("ánh", "Á")]
        [TestCase("ánh", "Á")]
        [TestCase("007Bond", "0")]
        [TestCase("!!!", "!")]
        [TestCase("   ", "?")]
        [TestCase("", "?")]
        [TestCase(null, "?")]
        public void FirstLetter_PrefersFirstLetterOrDigit(string name, string expected)
        {
            Assert.AreEqual(expected, LeaderboardEntryView.FirstLetter(name));
        }
    }

    /// <summary>Dựng canvas trong preview scene để test không làm bẩn scene đang mở.</summary>
    internal sealed class PreviewCanvas : IDisposable
    {
        private readonly Scene _scene;

        public PreviewCanvas()
        {
            _scene = EditorSceneManager.NewPreviewScene();
            Root = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(Root, _scene);
            var canvas = Root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)Root.transform).sizeDelta = new Vector2(1080f, 1920f);
        }

        public GameObject Root { get; }

        public T Spawn<T>(T prefab) where T : Component
        {
            return Object.Instantiate(prefab, Root.transform, false);
        }

        public void Dispose()
        {
            EditorSceneManager.ClosePreviewScene(_scene);
        }
    }

    [TestFixture]
    public class EntryViewRebindTests
    {
        private const float FrameDeltaTime = 1f / 60f;

        private PreviewCanvas _canvas;
        private LeaderboardEntryView _rowPrefab;
        private LeaderboardThemeConfig _theme;
        private LeaderboardTextConfig _text;
        private LeaderboardRenderContext _context;

        [SetUp]
        public void SetUp()
        {
            _canvas = new PreviewCanvas();
            _rowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LeaderboardEditorPaths.RowPrefabPath).GetComponent<LeaderboardEntryView>();
            _theme = ScriptableObject.CreateInstance<LeaderboardThemeConfig>();
            _text = ScriptableObject.CreateInstance<LeaderboardTextConfig>();
            _context = new LeaderboardRenderContext(new MotionSettings(), new LeaderboardVisualSettings(), _theme, _text, RankTierRule.Default);
        }

        [TearDown]
        public void TearDown()
        {
            _canvas.Dispose();
            Object.DestroyImmediate(_theme);
            Object.DestroyImmediate(_text);
        }

        private static RowState CreateRow(string playerId, string name, int rank)
        {
            return new RowState(BoardRow.ForEntry(new LeaderboardEntry(playerId, name, 1234, rank), true), 0f);
        }

        private void RenderAt(LeaderboardEntryView view, RowState row, double clock)
        {
            _context.Clock = clock;
            _context.DeltaTime = FrameDeltaTime;
            if (!ReferenceEquals(view.BoundRow, row) || view.BoundContentVersion != row.ContentVersion) view.Bind(row, _context);
            view.Render(row, Vector2.zero, 1f, 1f, 0f, _context);
        }

        [Test]
        public void FreshlyBoundView_ResumesPillAndShineAtSamePhaseAsLongRunningView()
        {
            RowState row = CreateRow("me", "You", 7);
            row.StartPill(PillContent.RankUp, 12, RankTier.Standard, 0.0, _context.Motion);
            row.StartShine(0.0, _context.Motion);
            LeaderboardEntryView longRunning = _canvas.Spawn(_rowPrefab);
            LeaderboardEntryView recycled = _canvas.Spawn(_rowPrefab);

            for (double clock = 0.0; clock < 0.2; clock += FrameDeltaTime) RenderAt(longRunning, row, clock);
            RenderAt(longRunning, row, 0.2);
            RenderAt(recycled, row, 0.2);

            var longRunningPill = (RectTransform)longRunning.PillGroup.transform;
            var recycledPill = (RectTransform)recycled.PillGroup.transform;
            Assert.IsTrue(longRunning.PillGroup.gameObject.activeSelf);
            Assert.IsTrue(recycled.PillGroup.gameObject.activeSelf, "View vừa được bind giữa chừng vẫn phải hiện pill");
            Assert.AreEqual(longRunningPill.anchoredPosition, recycledPill.anchoredPosition);
            Assert.AreEqual(longRunningPill.localScale.x, recycledPill.localScale.x, 1e-4f);
            Assert.AreEqual(longRunning.PillGroup.alpha, recycled.PillGroup.alpha, 1e-3f);
            Assert.AreEqual("12", recycled.PillText.text);
            Assert.IsTrue(recycled.ShineTransform.gameObject.activeSelf, "Vệt shine cũng phải tiếp tục");
            Assert.AreEqual(longRunning.ShineTransform.anchoredPosition.x, recycled.ShineTransform.anchoredPosition.x, 1e-3f);
        }

        [Test]
        public void PillInterruptedMidRise_NeverDriftsFromAuthoredPosition()
        {
            LeaderboardEntryView view = _canvas.Spawn(_rowPrefab);
            Vector2 authoredPosition = ((RectTransform)_rowPrefab.PillGroup.transform).anchoredPosition;
            RowState row = CreateRow("me", "You", 7);
            RowState other = new RowState(BoardRow.ForEntry(new LeaderboardEntry("other", "Other", 10, 8), false), 1f);
            MotionSettings motion = _context.Motion;

            row.StartPill(PillContent.RankUp, 3, RankTier.Standard, 0.0, motion);
            double midRise = motion.PillPopDuration + motion.PillHoldDuration + motion.PillRiseDuration * 0.5;
            RenderAt(view, row, midRise);
            Assert.Greater(((RectTransform)view.PillGroup.transform).anchoredPosition.y, authoredPosition.y);

            RenderAt(view, other, midRise + FrameDeltaTime);
            row.StartPill(PillContent.New, 0, RankTier.Standard, 10.0, motion);
            RenderAt(view, row, 10.0 + motion.PillPopDuration + 0.1);

            Assert.AreEqual(authoredPosition, ((RectTransform)view.PillGroup.transform).anchoredPosition);
        }

        [Test]
        public void HostileLongName_ShownLiterallyWithEllipsis()
        {
            LeaderboardEntryView view = _canvas.Spawn(_rowPrefab);
            RowState row = CreateRow("hacker", "<size=500><color=red>Nguyễn Hoàng Phương Uyên Siêu Cấp Vũ Trụ</color>", 3);

            RenderAt(view, row, 0.0);

            Assert.IsFalse(view.NameText.richText);
            Assert.AreEqual(TextOverflowModes.Ellipsis, view.NameText.overflowMode);
            StringAssert.Contains("<size=500>", view.NameText.text);
        }
    }

    [TestFixture]
    public class WidgetLifecycleTests
    {
        private const float FrameDeltaTime = 1f / 60f;
        private const int MaximumTicks = 3000;

        private PreviewCanvas _canvas;
        private LeaderboardWidget _widget;
        private MockLeaderboardService _service;
        private ManualScoreSource _scoreSource;
        private LeaderboardBoard _board;

        private sealed class ThrowingSink : ILeaderboardFeedbackSink
        {
            public int CallCount;

            public void OnBeat(LeaderboardBeat beat, in LeaderboardBeatContext context)
            {
                CallCount++;
                throw new InvalidOperationException("sink hỏng");
            }
        }

        /// <summary>Bọc một backend; lượt gọi kế tiếp ném OperationCanceledException KHÔNG gắn với token nào (huỷ lạc).</summary>
        private sealed class StrayCancellationLeaderboardService : ILeaderboardService
        {
            private readonly ILeaderboardService _inner;

            public StrayCancellationLeaderboardService(ILeaderboardService inner)
            {
                _inner = inner;
            }

            public bool ThrowOnNextCall;

            public string LocalPlayerId => _inner.LocalPlayerId;
            public string SeasonKey => _inner.SeasonKey;

            public Task<LeaderboardEntry> GetLocalEntryAsync(CancellationToken cancellationToken)
            {
                ThrowIfRequested();
                return _inner.GetLocalEntryAsync(cancellationToken);
            }

            public Task<LeaderboardEntry> SubmitScoreAsync(long score, CancellationToken cancellationToken)
            {
                ThrowIfRequested();
                return _inner.SubmitScoreAsync(score, cancellationToken);
            }

            public Task<IReadOnlyList<LeaderboardEntry>> GetRangeAsync(int offset, int limit, CancellationToken cancellationToken)
            {
                ThrowIfRequested();
                return _inner.GetRangeAsync(offset, limit, cancellationToken);
            }

            private void ThrowIfRequested()
            {
                if (!ThrowOnNextCall) return;
                ThrowOnNextCall = false;
                throw new OperationCanceledException("Backend bỏ lượt gọi cũ — không ai huỷ lượt trình bày.");
            }
        }

        [SetUp]
        public void SetUp()
        {
            _canvas = new PreviewCanvas();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LeaderboardEditorPaths.WidgetPrefabPath).GetComponent<LeaderboardWidget>();
            _widget = _canvas.Spawn(prefab);
            _service = new MockLeaderboardService(new MockLeaderboardOptions { BotCount = 3000, LatencyMilliseconds = 0 });
            _scoreSource = new ManualScoreSource("test");
            _board = new LeaderboardBoard(new LeaderboardBoardSettings("widget-test", FetchWindowSettings.Default, RankTierRule.Default),
                                          _service, _scoreSource, new InMemoryLeaderboardSnapshotStore());
        }

        [TearDown]
        public void TearDown()
        {
            _canvas.Dispose();
        }

        private void PrepareRankUp(int startRank, int targetRank)
        {
            _service.SetLocalScore(_service.ScoreToReachRank(startRank));
            _board.MarkRevealed(RankChange.Browse(_service.GetLocalEntryAsync(CancellationToken.None).Result));
            _scoreSource.SetScore(_service.ScoreToReachRank(targetRank));
        }

        private Task<LeaderboardPresentResult> Present(BoardPresentMode mode)
        {
            _widget.Arm();
            return _widget.PresentAsync(new LeaderboardPresentRequest(_board, mode), CancellationToken.None);
        }

        private LeaderboardPresentResult RunToCompletion(Task<LeaderboardPresentResult> task)
        {
            for (int tick = 0; tick < MaximumTicks && !task.IsCompleted; tick++) _widget.AdvanceForTests(FrameDeltaTime);
            Assert.IsTrue(task.IsCompleted, "Trình bày phải kết thúc");
            return task.Result;
        }

        [Test]
        public void RankUp_Completes_AndMarksChangeRevealed()
        {
            PrepareRankUp(120, 108);

            Task<LeaderboardPresentResult> task = Present(BoardPresentMode.RevealIfPending);
            Assert.IsNotNull(_widget.CurrentTimeline, "Dữ liệu Mock đồng bộ nên timeline phải bắt đầu ngay");
            LeaderboardPresentResult result = RunToCompletion(task);

            Assert.AreEqual(PresentOutcome.Completed, result.Outcome);
            Assert.AreEqual(RankChangeKind.RankUp, result.Change.Kind);
            Assert.IsFalse(_board.HasUnrevealedChange);
            Assert.IsFalse(_widget.SkipCatcher.gameObject.activeSelf);
        }

        [Test]
        public void SkipMidClimb_LocalViewStillShowsPillAfterLanding()
        {
            PrepareRankUp(120, 108);
            Task<LeaderboardPresentResult> task = Present(BoardPresentMode.RevealIfPending);
            for (int tick = 0; tick < MaximumTicks && _widget.CurrentTimeline.Phase != RevealPhase.Climb; tick++) _widget.AdvanceForTests(FrameDeltaTime);
            _widget.AdvanceForTests(0.2f);

            _widget.Skip();
            for (int tick = 0; tick < 3; tick++) _widget.AdvanceForTests(FrameDeltaTime);

            RowState localRow = _widget.CurrentModel.LocalRow;
            LeaderboardEntryView localView = _widget.ScrollView.GetActiveView(localRow);
            Assert.IsNotNull(localView, "View của row người chơi phải được ghim sau skip");
            Assert.IsTrue(localView.PillGroup.gameObject.activeSelf, "Pill ▲N phải hiện sau skip (lỗi gốc của bản tham khảo)");
            Assert.IsTrue(localView.ShineTransform.gameObject.activeSelf, "Shine phải hiện sau skip");
            Assert.AreEqual(PresentOutcome.Skipped, RunToCompletion(task).Outcome);
        }

        [Test]
        public void BackendFailure_ReportsFailedAndShowsError_ThenRetryWorks()
        {
            _service.FailNextCall();

            Task<LeaderboardPresentResult> failed = Present(BoardPresentMode.Browse);

            Assert.IsTrue(failed.IsCompleted);
            Assert.AreEqual(PresentOutcome.Failed, failed.Result.Outcome);
            Assert.IsTrue(_widget.StatusView.IsShowingError);

            Task<LeaderboardPresentResult> retried = _widget.PresentAsync(new LeaderboardPresentRequest(_board, BoardPresentMode.Browse), CancellationToken.None);
            Assert.AreEqual(PresentOutcome.Completed, RunToCompletion(retried).Outcome);
            Assert.IsFalse(_widget.StatusView.IsShowingError);
        }

        /// <summary>
        /// Adapter ném <see cref="OperationCanceledException"/> mà không ai huỷ lượt trình bày (vd dịch vụ bỏ lượt gọi cũ sau khi xoá
        /// dữ liệu). Trước đây widget coi là huỷ: kết thúc Cancelled, không tắt Loading, không có nút thử lại — đứng mãi ở "Loading".
        /// </summary>
        [Test]
        public void StrayCancellationFromAdapter_ReportsFailedAndShowsError_ThenRetryWorks()
        {
            var strayService = new StrayCancellationLeaderboardService(_service) { ThrowOnNextCall = true };
            var board = new LeaderboardBoard(new LeaderboardBoardSettings("widget-stray-cancel", FetchWindowSettings.Default, RankTierRule.Default),
                                             strayService, _scoreSource, new InMemoryLeaderboardSnapshotStore());
            _widget.Arm();

            Task<LeaderboardPresentResult> failed = _widget.PresentAsync(new LeaderboardPresentRequest(board, BoardPresentMode.Browse), CancellationToken.None);

            Assert.IsTrue(failed.IsCompleted, "Không được đứng mãi ở trạng thái đang tải");
            Assert.AreEqual(PresentOutcome.Failed, failed.Result.Outcome);
            Assert.IsInstanceOf<OperationCanceledException>(failed.Result.Error);
            Assert.IsTrue(_widget.StatusView.IsShowingError, "Phải hiện lỗi kèm nút thử lại");

            Task<LeaderboardPresentResult> retried = _widget.PresentAsync(new LeaderboardPresentRequest(board, BoardPresentMode.Browse), CancellationToken.None);
            Assert.AreEqual(PresentOutcome.Completed, RunToCompletion(retried).Outcome);
            Assert.IsFalse(_widget.StatusView.IsShowingError);
        }

        /// <summary>Huỷ thật bằng token của host vẫn là Cancelled, không hiện lỗi.</summary>
        [Test]
        public void HostTokenCancelled_ReportsCancelled_WithoutError()
        {
            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            _widget.Arm();

            Task<LeaderboardPresentResult> task = _widget.PresentAsync(new LeaderboardPresentRequest(_board, BoardPresentMode.Browse), cancellation.Token);

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(PresentOutcome.Cancelled, task.Result.Outcome);
            Assert.IsFalse(_widget.StatusView.IsShowingError);
        }

        [Test]
        public void DisarmBeforeLanding_CancelsWithoutMarkingRevealed()
        {
            PrepareRankUp(120, 108);
            Task<LeaderboardPresentResult> task = Present(BoardPresentMode.RevealIfPending);
            _widget.AdvanceForTests(0.1f);

            _widget.Disarm();

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(PresentOutcome.Cancelled, task.Result.Outcome);
            Assert.IsTrue(_board.HasUnrevealedChange, "Chưa tới nhịp hạ cánh thì lần mở sau phải diễn lại");
        }

        /// <summary>
        /// Trình bày lại trên widget ĐANG armed (host không gọi Arm lại — quảng cáo cộng điểm, Retry) sau khi màn diễn trước đã
        /// xong, và dữ liệu mới về chậm một nhịp (host chưa sẵn sàng): lượt mới KHÔNG được kết thúc bằng màn diễn cũ.
        ///
        /// <para>Trước đây CancelPresent để lại timeline cũ đã xong; Update kế tiếp thấy "timeline xong + có lượt đang chờ" và kết
        /// thúc lượt MỚI bằng RankChange CŨ. Màn diễn thật tới sau đó không còn completion nào nên không bao giờ FinishReveal: skip
        /// catcher bật, camera bám và row mình bị ghim, cuộn tay bị chặn, reveal lease kẹt tới Disarm. Test cũ không thấy vì luôn
        /// Arm trước và Mock trả đồng bộ, nên timeline mới thay chỗ trước cả tick đầu.</para>
        /// </summary>
        [UnityTest]
        public IEnumerator PresentAgainWhileArmed_DoesNotFinishTheNewRequestWithTheOldTimeline()
        {
            PrepareRankUp(120, 108);
            LeaderboardPresentResult first = RunToCompletion(Present(BoardPresentMode.RevealIfPending));
            Assert.AreEqual(PresentOutcome.Completed, first.Outcome);
            Assert.IsTrue(_widget.IsArmed, "Tiền đề hỏng: widget phải còn armed sau khi diễn xong.");

            _scoreSource.SetScore(_service.ScoreToReachRank(100));
            var hostReady = new TaskCompletionSource<bool>();
            Task<LeaderboardPresentResult> second = _widget.PresentAsync(
                new LeaderboardPresentRequest(_board, BoardPresentMode.RevealIfPending, hostReady.Task), CancellationToken.None);

            for (int tick = 0; tick < 10; tick++) _widget.AdvanceForTests(FrameDeltaTime);
            Assert.IsFalse(second.IsCompleted, "Lượt mới đã bị kết thúc bằng màn diễn cũ trước khi dữ liệu mới kịp dựng.");
            Assert.IsNull(_widget.CurrentTimeline, "Màn diễn cũ vẫn nằm trong tay widget sau khi có lượt trình bày mới.");
            Assert.DoesNotThrow(() => _widget.ReleasePodiumHold());
            Assert.DoesNotThrow(() => _widget.ReleaseRevealHold());

            hostReady.SetResult(true);
            for (int frame = 0; frame < 120 && _widget.CurrentTimeline == null && !second.IsCompleted; frame++) yield return null;
            Assert.IsNotNull(_widget.CurrentTimeline, "Host đã sẵn sàng mà màn diễn mới chưa bắt đầu.");

            LeaderboardPresentResult result = RunToCompletion(second);
            Assert.AreEqual(PresentOutcome.Completed, result.Outcome);
            Assert.AreEqual(RankChangeKind.RankUp, result.Change.Kind);
            Assert.AreEqual(first.Change.ToRank, result.Change.FromRank, "Lượt mới phải kết thúc bằng thay đổi MỚI, không phải thay đổi cũ.");
            Assert.AreEqual(100, result.Change.ToRank);
            Assert.IsFalse(_board.HasUnrevealedChange);
            Assert.IsFalse(_widget.SkipCatcher.gameObject.activeSelf, "Skip catcher kẹt bật sau màn diễn thứ hai.");
            Assert.IsFalse(_widget.ScrollView.FollowLocalRow, "Camera còn bám row mình sau màn diễn thứ hai.");
            Assert.IsFalse(_widget.ScrollView.PinLocalRow, "Row mình còn bị ghim sau màn diễn thứ hai.");
        }

        [Test]
        public void FaultySink_DoesNotBreakReveal()
        {
            PrepareRankUp(120, 108);
            var sink = new ThrowingSink();
            _widget.SetFeedbackSinks(new ILeaderboardFeedbackSink[] { sink });

            LeaderboardPresentResult result = RunToCompletion(Present(BoardPresentMode.RevealIfPending));

            Assert.AreEqual(PresentOutcome.Completed, result.Outcome);
            Assert.Greater(sink.CallCount, 0);
        }

        [Test]
        public void Browse_CompletesImmediately_WithoutTimeline()
        {
            PrepareRankUp(120, 108);

            Task<LeaderboardPresentResult> task = Present(BoardPresentMode.Browse);

            Assert.IsTrue(task.IsCompleted);
            Assert.IsNull(_widget.CurrentTimeline);
            Assert.Greater(_widget.ScrollView.ActiveViewCount, 0);
        }
    }
}
