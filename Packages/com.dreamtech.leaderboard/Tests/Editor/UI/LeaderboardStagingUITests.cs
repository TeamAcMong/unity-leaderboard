using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamTech.Leaderboard.EditorTools;
using DreamTech.Leaderboard.ViewModel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DreamTech.Leaderboard.UI.Tests
{
    /// <summary>
    /// <c>LeaderboardVisualSettings.StageListBeforeHostReady</c>: list dựng sẵn ngay khi dữ liệu về — row người chơi ở ô TRƯỚC màn
    /// diễn, sát mép trên khung nhìn (đỉnh list khi row đó nằm sau phần host trình bày), mọi row ở tư thế đầu đợt trượt, model KHÔNG
    /// được tick — rồi lượt trình bày thật dùng lại đúng model đó khi host sẵn sàng. Cờ tắt thì nội dung trống tới lúc host sẵn sàng.
    ///
    /// <para>Widget mẫu của package được bơm một bản sao config chuyển động (qua SerializedObject — đúng đường một game gán asset của
    /// nó), nên prefab, pool và camera là thật.</para>
    /// </summary>
    [TestFixture]
    public class LeaderboardStagingUITests
    {
        private const float FrameDeltaTime = 1f / 60f;
        private const int MaximumTicks = 3000;
        private const int MaximumWaitFrames = 120;
        private const float ScrollTolerance = 0.01f;

        private PreviewCanvas _canvas;
        private readonly List<Object> _createdAssets = new List<Object>();
        private LeaderboardWidget _widget;
        private MockLeaderboardService _service;
        private ManualScoreSource _scoreSource;
        private LeaderboardBoard _board;
        private int _stagedCount;

        [SetUp]
        public void SetUp()
        {
            _canvas = new PreviewCanvas();
            _stagedCount = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _canvas.Dispose();
            for (int index = 0; index < _createdAssets.Count; index++)
            {
                if (_createdAssets[index] != null) Object.DestroyImmediate(_createdAssets[index]);
            }
            _createdAssets.Clear();
        }

        private void CreateWidget(bool stageListBeforeHostReady, int hostPresentedTopRanks = 0)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LeaderboardEditorPaths.WidgetPrefabPath).GetComponent<LeaderboardWidget>();
            _widget = _canvas.Spawn(prefab);

            var widgetObject = new SerializedObject(_widget);
            SerializedProperty motionProperty = widgetObject.FindProperty("motionConfig");
            var source = motionProperty.objectReferenceValue as LeaderboardMotionConfig;
            LeaderboardMotionConfig motion = source != null ? Object.Instantiate(source) : ScriptableObject.CreateInstance<LeaderboardMotionConfig>();
            _createdAssets.Add(motion);
            var motionObject = new SerializedObject(motion);
            motionObject.FindProperty("visuals.StageListBeforeHostReady").boolValue = stageListBeforeHostReady;
            motionObject.FindProperty("timeline.HostPresentedTopRanks").intValue = hostPresentedTopRanks;
            motionObject.ApplyModifiedPropertiesWithoutUndo();
            motionProperty.objectReferenceValue = motion;
            widgetObject.ApplyModifiedPropertiesWithoutUndo();

            _widget.ListStaged += () => _stagedCount++;
            _service = new MockLeaderboardService(new MockLeaderboardOptions { BotCount = 3000, LatencyMilliseconds = 0 });
            _scoreSource = new ManualScoreSource("staging-ui");
            _board = new LeaderboardBoard(new LeaderboardBoardSettings("staging-ui", FetchWindowSettings.Default, RankTierRule.Default),
                                          _service, _scoreSource, new InMemoryLeaderboardSnapshotStore());
        }

        private void PrepareRankUp(int startRank, int targetRank)
        {
            _service.SetLocalScore(_service.ScoreToReachRank(startRank));
            _board.MarkRevealed(RankChange.Browse(_service.GetLocalEntryAsync(CancellationToken.None).Result));
            _scoreSource.SetScore(_service.ScoreToReachRank(targetRank));
        }

        private Task<LeaderboardPresentResult> PresentWith(Task hostReady)
        {
            _widget.Arm();
            return _widget.PresentAsync(new LeaderboardPresentRequest(_board, BoardPresentMode.RevealIfPending, hostReady),
                                        CancellationToken.None);
        }

        private float Scroll => _widget.ScrollView.Content.anchoredPosition.y;

        private float MaximumScroll =>
            Mathf.Max(0f, _widget.ScrollView.Content.sizeDelta.y - _widget.ScrollView.Viewport.rect.height);

        private float TopAlignedScroll(RowState row)
        {
            return Mathf.Clamp(_widget.ScrollView.Layout.SlotToTop(row.Slot), 0f, MaximumScroll);
        }

        private float CenteredScroll(RowState row)
        {
            return _widget.ScrollView.Layout.CenteredScroll(row.Slot, _widget.ScrollView.Viewport.rect.height, MaximumScroll);
        }

        private static void AssertEveryRowWaitsAtIntroStart(BoardModel model, string label)
        {
            foreach (RowState row in model.Rows)
            {
                Assert.IsTrue(row.IsIntroPlaying, label + ": row " + row.DisplayRank + " không đứng ở đầu đợt trượt.");
                Assert.AreEqual(0f, row.IntroAlpha, label + ": row " + row.DisplayRank + " đã hiện trước khi host sẵn sàng.");
            }
        }

        private void AssertActiveViewsInvisible(string label)
        {
            foreach (Transform child in _widget.ScrollView.Content)
            {
                var view = child.GetComponent<LeaderboardEntryView>();
                if (view == null || !child.gameObject.activeSelf) continue;
                Assert.AreEqual(0f, view.GetComponent<CanvasGroup>().alpha, label + ": một thanh đã hiện trước khi host sẵn sàng.");
            }
        }

        /// <summary>Cờ tắt (mặc định): trước khi host sẵn sàng list trống, không bắn ListStaged — y hệt trước khi có cờ.</summary>
        [Test]
        public void FlagOff_ListStaysEmptyUntilHostReady()
        {
            CreateWidget(stageListBeforeHostReady: false);
            PrepareRankUp(120, 108);
            var hostReady = new TaskCompletionSource<bool>();

            PresentWith(hostReady.Task);
            for (int tick = 0; tick < 5; tick++) _widget.AdvanceForTests(FrameDeltaTime);

            Assert.IsNull(_widget.ScrollView.Model, "Cờ tắt mà list đã có model trước khi host sẵn sàng.");
            Assert.AreEqual(0, _widget.ScrollView.ActiveViewCount);
            Assert.AreEqual(0, _stagedCount, "Cờ tắt mà widget vẫn bắn ListStaged.");
        }

        /// <summary>
        /// Cờ bật, host chưa sẵn sàng: list dựng sẵn ở ô CŨ của người chơi (sát mép trên khung nhìn), mọi thanh ẩn ở đầu đợt trượt,
        /// model không được tick. Host sẵn sàng: CÙNG model được dùng lại, list canh giữa lại ô cũ và màn diễn chạy trọn.
        /// </summary>
        [UnityTest]
        public IEnumerator FlagOn_StagesListAtThePreRevealRow_ThenRevealsWithTheSameModel()
        {
            CreateWidget(stageListBeforeHostReady: true);
            PrepareRankUp(120, 108);
            var hostReady = new TaskCompletionSource<bool>();

            Task<LeaderboardPresentResult> task = PresentWith(hostReady.Task);

            BoardModel staged = _widget.ScrollView.Model;
            Assert.IsNotNull(staged, "Dữ liệu Mock đồng bộ mà list chưa được dựng sẵn.");
            Assert.AreEqual(1, _stagedCount, "ListStaged phải bắn đúng một lần.");
            Assert.IsNull(_widget.CurrentModel, "Model dựng sẵn không được nằm trong tay widget (nó sẽ bị tick).");
            RowState local = staged.LocalRow;
            Assert.Greater(local.Slot, staged.IndexOf(local), "Row người chơi phải đứng ở ô TRƯỚC màn diễn (dưới ô đích).");
            Assert.AreEqual(TopAlignedScroll(local), Scroll, ScrollTolerance, "Mép trên ô cũ phải sát mép trên khung nhìn.");
            Assert.AreNotEqual(CenteredScroll(local), TopAlignedScroll(local), "Tiền đề hỏng: sát mép trên trùng canh giữa.");
            Assert.Greater(_widget.ScrollView.ActiveViewCount, 0);
            AssertEveryRowWaitsAtIntroStart(staged, "Dựng sẵn");
            AssertActiveViewsInvisible("Dựng sẵn");

            for (int tick = 0; tick < 30; tick++) _widget.AdvanceForTests(FrameDeltaTime);
            AssertEveryRowWaitsAtIntroStart(staged, "Sau 30 khung chờ host");
            Assert.AreEqual(TopAlignedScroll(local), Scroll, ScrollTolerance, "List trôi khỏi chỗ dựng sẵn trong lúc chờ host.");

            hostReady.SetResult(true);
            for (int frame = 0; frame < MaximumWaitFrames && _widget.CurrentTimeline == null && !task.IsCompleted; frame++) yield return null;
            Assert.IsNotNull(_widget.CurrentTimeline, "Host đã sẵn sàng mà màn diễn chưa bắt đầu.");
            Assert.AreSame(staged, _widget.CurrentModel, "Lượt trình bày thật phải dùng lại model dựng sẵn.");
            Assert.AreSame(staged, _widget.ScrollView.Model);
            Assert.AreEqual(CenteredScroll(local), Scroll, ScrollTolerance, "Host sẵn sàng thì list canh giữa lại ô cũ.");
            Assert.AreEqual(1, _stagedCount);

            for (int tick = 0; tick < MaximumTicks && !task.IsCompleted; tick++) _widget.AdvanceForTests(FrameDeltaTime);
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(PresentOutcome.Completed, task.Result.Outcome);
            Assert.AreEqual(RankChangeKind.RankUp, task.Result.Change.Kind);
            Assert.IsFalse(_board.HasUnrevealedChange);
        }

        /// <summary>Row người chơi đang nằm trong phần host trình bày (hạng 2 với bục ba chỗ): list dựng sẵn ở ĐỈNH (cuộn 0).</summary>
        [Test]
        public void FlagOn_LocalRowBehindTheHostRegion_StagesAtTheTop()
        {
            CreateWidget(stageListBeforeHostReady: true, hostPresentedTopRanks: 3);
            PrepareRankUp(2, 1);
            var hostReady = new TaskCompletionSource<bool>();

            PresentWith(hostReady.Task);

            BoardModel staged = _widget.ScrollView.Model;
            Assert.IsNotNull(staged);
            Assert.Less(staged.LocalRow.Slot, staged.HiddenLeadingSlots, "Tiền đề hỏng: row mình phải nằm sau bục.");
            Assert.AreEqual(0f, Scroll, ScrollTolerance, "Row mình sau bục thì list dựng sẵn ở đỉnh.");
        }

        /// <summary>Host đã sẵn sàng khi dữ liệu về (không có gì để chờ): đi thẳng vào màn diễn, không dựng sẵn.</summary>
        [Test]
        public void FlagOn_HostAlreadyReady_ShowsDirectlyWithoutStaging()
        {
            CreateWidget(stageListBeforeHostReady: true);
            PrepareRankUp(120, 108);

            Task<LeaderboardPresentResult> task = PresentWith(null);

            Assert.AreEqual(0, _stagedCount, "Host không bắt chờ mà widget vẫn dựng sẵn.");
            Assert.IsNotNull(_widget.CurrentTimeline, "Dữ liệu Mock đồng bộ nên màn diễn phải bắt đầu ngay.");
            for (int tick = 0; tick < MaximumTicks && !task.IsCompleted; tick++) _widget.AdvanceForTests(FrameDeltaTime);
            Assert.AreEqual(PresentOutcome.Completed, task.Result.Outcome);
        }

        /// <summary>Lượt bỏ đợt trượt vào (<c>SkipIntro</c>) không dựng sẵn: thanh dựng ở tư thế đứng không có "tư thế đầu" nào để chờ.</summary>
        [Test]
        public void FlagOn_SkipIntroRequest_DoesNotStage()
        {
            CreateWidget(stageListBeforeHostReady: true);
            PrepareRankUp(120, 108);
            var hostReady = new TaskCompletionSource<bool>();

            _widget.Arm();
            _widget.PresentAsync(new LeaderboardPresentRequest(_board, BoardPresentMode.RevealIfPending, hostReady.Task, skipIntro: true),
                                 CancellationToken.None);

            Assert.AreEqual(0, _stagedCount);
            Assert.IsNull(_widget.ScrollView.Model);
        }
    }
}
