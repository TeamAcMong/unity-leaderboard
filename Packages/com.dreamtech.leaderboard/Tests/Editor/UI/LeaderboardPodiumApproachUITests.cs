using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamTech.Leaderboard.EditorTools;
using DreamTech.Leaderboard.ViewModel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DreamTech.Leaderboard.UI.Tests
{
    /// <summary>
    /// Phía list của cú tiếp cận bục kiểu cuộn (<c>MotionSettings.PodiumApproachScrollSpeed</c>, 0.6.0): camera mở màn canh giữa
    /// ô xuất phát (kể cả ô ranh giới), thời lượng = quãng cuộn / tốc độ, camera và row mình đi theo CÙNG một tiến độ (đường đi
    /// trên màn hình thẳng tuyến tính), và lớp nổi (<see cref="LeaderboardScrollView.FloatingRowLayer"/>) vẽ row mình ngoài mask
    /// đúng chỗ list sẽ vẽ nó, rồi trả view về pool khi host giành thanh.
    ///
    /// <para>Widget mẫu của package được bơm cấu hình qua SerializedObject — đúng đường một game gán asset của nó.</para>
    /// </summary>
    [TestFixture]
    public class LeaderboardPodiumApproachUITests
    {
        private const float FrameDeltaTime = 1f / 60f;
        private const int MaximumTicks = 3000;
        private const int TopRanks = 3;

        /// <summary>Tốc độ cuộn danh sách của game tham chiếu (đơn vị canvas / giây).</summary>
        private const float ReferenceScrollSpeed = 2500f;

        /// <summary>
        /// Dải lớn phía trên ô 0 như một game có bục — với bố cục mẫu, canh giữa các ô ≤ 5 đã là cuộn 0 và mọi test "cuộn" ở đây
        /// sẽ xanh cả khi tính năng bị gỡ (xem <c>LeaderboardPodiumUITests.PodiumBandTopPadding</c>).
        /// </summary>
        private const float PodiumBandTopPadding = 900f;

        private PreviewCanvas _canvas;
        private readonly List<Object> _createdAssets = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _canvas = new PreviewCanvas();
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

        private sealed class Harness
        {
            public LeaderboardWidget Widget;
            public MockLeaderboardService Service;
            public ManualScoreSource ScoreSource;
            public LeaderboardBoard Board;
            public RectTransform FloatingLayer;
            public readonly List<LeaderboardBeat> Beats = new List<LeaderboardBeat>();

            public LeaderboardScrollView ScrollView => Widget.ScrollView;
            public BoardModel Model => Widget.CurrentModel;
            public RevealTimeline Timeline => Widget.CurrentTimeline;
            public float Scroll => ScrollView.Content.anchoredPosition.y;
            public VirtualListLayout Layout => ScrollView.Layout;

            public Task<LeaderboardPresentResult> PresentRankUp(int startRank, int targetRank)
            {
                Service.SetLocalScore(Service.ScoreToReachRank(startRank));
                Board.MarkRevealed(RankChange.Browse(Service.GetLocalEntryAsync(CancellationToken.None).Result));
                ScoreSource.SetScore(Service.ScoreToReachRank(targetRank));
                Widget.Arm();
                return Widget.PresentAsync(new LeaderboardPresentRequest(Board, BoardPresentMode.RevealIfPending),
                                           CancellationToken.None);
            }

            public void Advance(int ticks)
            {
                for (int tick = 0; tick < ticks; tick++) Widget.AdvanceForTests(FrameDeltaTime);
            }
        }

        /// <summary>Ghi nhịp; tuỳ chọn giành thanh của mình khỏi list đúng nhịp PodiumTakeover như host thật làm.</summary>
        private sealed class RecordingSink : ILeaderboardFeedbackSink
        {
            private readonly Harness _harness;
            private readonly bool _hidesLocalRowOnTakeover;

            public RecordingSink(Harness harness, bool hidesLocalRowOnTakeover)
            {
                _harness = harness;
                _hidesLocalRowOnTakeover = hidesLocalRowOnTakeover;
            }

            public void OnBeat(LeaderboardBeat beat, in LeaderboardBeatContext context)
            {
                _harness.Beats.Add(beat);
                if (_hidesLocalRowOnTakeover && beat == LeaderboardBeat.PodiumTakeover) _harness.Model.LocalRow.IsHiddenFromList = true;
            }
        }

        /// <summary>Đường cuộn của game tham chiếu (hai khoá, khoá cuối có trọng số phía vào).</summary>
        private static AnimationCurve ReferenceScrollCurve()
        {
            return new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f),
                                      new Keyframe(1f, 1f, 0.09481128f, 0.09481128f, 0.47263688f, 0f)
                                      {
                                          weightedMode = WeightedMode.In,
                                      });
        }

        private Harness CreateHarness(float scrollSpeed, bool withFloatingLayer = false, bool hidesLocalRowOnTakeover = false,
                                      float widgetScale = 1f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LeaderboardEditorPaths.WidgetPrefabPath).GetComponent<LeaderboardWidget>();
            var harness = new Harness { Widget = _canvas.Spawn(prefab) };
            harness.Widget.transform.localScale = new Vector3(widgetScale, widgetScale, 1f);

            var scrollViewObject = new SerializedObject(harness.Widget.ScrollView);
            scrollViewObject.FindProperty("topPadding").floatValue = PodiumBandTopPadding;
            if (withFloatingLayer)
            {
                // Lớp nổi là con của GỐC canvas (ngoài widget, ngoài mask) — khác cha, khác tỉ lệ với Content khi widget bị thu.
                var layerObject = new GameObject("FloatingRow", typeof(RectTransform));
                layerObject.transform.SetParent(_canvas.Root.transform, false);
                harness.FloatingLayer = (RectTransform)layerObject.transform;
                scrollViewObject.FindProperty("floatingRowLayer").objectReferenceValue = harness.FloatingLayer;
            }
            scrollViewObject.ApplyModifiedPropertiesWithoutUndo();

            var widgetObject = new SerializedObject(harness.Widget);
            SerializedProperty motionProperty = widgetObject.FindProperty("motionConfig");
            var source = motionProperty.objectReferenceValue as LeaderboardMotionConfig;
            LeaderboardMotionConfig motion = source != null ? Object.Instantiate(source) : ScriptableObject.CreateInstance<LeaderboardMotionConfig>();
            _createdAssets.Add(motion);

            var motionObject = new SerializedObject(motion);
            motionObject.FindProperty("timeline.HostPresentedTopRanks").intValue = TopRanks;
            motionObject.FindProperty("timeline.HostHoldTimeout").floatValue = 100f;
            motionObject.FindProperty("timeline.PodiumApproachScrollSpeed").floatValue = scrollSpeed;
            motionObject.FindProperty("timeline.DeferPodiumApproachPasses").boolValue = true;
            motionObject.FindProperty("timeline.ClimbTickInterval").floatValue = 0.18f;
            motionObject.FindProperty("climbCurve").animationCurveValue = ReferenceScrollCurve();
            motionObject.ApplyModifiedPropertiesWithoutUndo();
            motionProperty.objectReferenceValue = motion;
            widgetObject.ApplyModifiedPropertiesWithoutUndo();

            harness.Service = new MockLeaderboardService(new MockLeaderboardOptions { BotCount = 3000, LatencyMilliseconds = 0 });
            harness.ScoreSource = new ManualScoreSource("podium-approach-ui");
            harness.Board = new LeaderboardBoard(new LeaderboardBoardSettings("podium-approach-ui", FetchWindowSettings.Default, RankTierRule.Default),
                                                 harness.Service, harness.ScoreSource, new InMemoryLeaderboardSnapshotStore());
            harness.Widget.SetFeedbackSinks(new ILeaderboardFeedbackSink[] { new RecordingSink(harness, hidesLocalRowOnTakeover) });
            return harness;
        }

        private static float CenteredScroll(Harness harness, float slot)
        {
            LeaderboardScrollView scrollView = harness.ScrollView;
            float viewportHeight = scrollView.Viewport.rect.height;
            float maximumScroll = harness.Layout.MaximumScroll(scrollView.Content.sizeDelta.y, viewportHeight);
            return harness.Layout.CenteredScroll(slot, viewportHeight, maximumScroll);
        }

        private static int CountEntryViews(Transform parent)
        {
            return parent.GetComponentsInChildren<LeaderboardEntryView>(true).Length;
        }

        // ---------------------------------------------------------------- Camera

        /// <summary>
        /// Bắt đầu ĐÚNG ô ranh giới (hạng 4): cờ bật thì camera mở màn canh giữa ô đó (không phải đỉnh list như khi cờ tắt — xem
        /// <c>LeaderboardPodiumUITests.StartAtOrAbovePodiumBoundary_CameraStaysAtTop</c>) và báo đúng chỗ cuộn đó cho model.
        /// </summary>
        [Test]
        public void IntroCamera_CentersTheStartRow_EvenAtTheBoundary()
        {
            Harness harness = CreateHarness(ReferenceScrollSpeed);
            harness.PresentRankUp(3, 1);

            Assert.IsTrue(harness.Model.HasPodiumApproach, "Tiền đề hỏng: lên bục từ ô ranh giới phải có cú tiếp cận.");
            Assert.AreEqual(TopRanks, harness.Model.LocalRow.Slot, 1e-4f, "Tiền đề hỏng: phải bắt đầu ở ô ranh giới.");
            float centered = CenteredScroll(harness, TopRanks);
            Assert.Greater(centered, 20f, "Tiền đề hỏng: canh giữa ô ranh giới đã là cuộn 0 — test không đo được gì.");
            Assert.AreEqual(centered, harness.Scroll, 0.01f, "Mở màn phải canh giữa ô xuất phát.");
            Assert.AreEqual(harness.Scroll, harness.Model.PodiumApproachStartScroll, 0.01f, "List phải báo đúng chỗ cuộn mở màn.");
        }

        /// <summary>
        /// #12 → #2: cú tiếp cận dài đúng quãng cuộn / 2500 (± 1 frame); ở MỌI tick camera = lerp(chỗ mở màn, 0, tiến độ) và
        /// khoảng cách row mình tới mép trên khung nhìn đi tuyến tính theo CHÍNH tiến độ đó — đường thẳng trên màn hình, kể cả khi
        /// cấu hình đang bám mềm (FollowSmoothTime 0,12 của widget mẫu). Tới cổng thì camera ở đỉnh list.
        /// </summary>
        [Test]
        public void Approach_CameraAndRowShareOneProgress_OnAStraightScreenPath()
        {
            Harness harness = CreateHarness(ReferenceScrollSpeed);
            harness.PresentRankUp(11, 1);
            BoardModel model = harness.Model;
            float startScroll = harness.Scroll;
            Assert.Greater(startScroll, 100f, "Tiền đề hỏng: row mình phải xuất phát xa đỉnh list.");
            Assert.AreEqual(startScroll, model.PodiumApproachStartScroll, 0.01f);
            float startSlot = harness.Timeline.Plan.StartSlot;
            float startDistanceFromViewportTop = harness.Layout.SlotToCenter(startSlot) - startScroll;
            float boundaryDistanceFromViewportTop = harness.Layout.SlotToCenter(TopRanks);

            int approachTicks = 0;
            for (int tick = 0; tick < MaximumTicks && harness.Timeline.Phase != RevealPhase.PodiumHold; tick++)
            {
                harness.Advance(1);
                if (harness.Timeline.Phase != RevealPhase.Climb) continue;
                approachTicks++;
                float progress = model.PodiumApproachProgress;
                Assert.AreEqual(Mathf.LerpUnclamped(startScroll, 0f, progress), harness.Scroll, 0.02f,
                                "Tick " + tick + ": camera không đi theo tiến độ của row mình.");
                float distanceFromViewportTop = harness.Layout.SlotToCenter(model.LocalRow.Slot) - harness.Scroll;
                Assert.AreEqual(Mathf.LerpUnclamped(startDistanceFromViewportTop, boundaryDistanceFromViewportTop, progress),
                                distanceFromViewportTop, 0.05f, "Tick " + tick + ": row mình không đi thẳng trên màn hình.");
            }

            Assert.AreEqual(RevealPhase.PodiumHold, harness.Timeline.Phase);
            Assert.AreEqual(startScroll / ReferenceScrollSpeed / FrameDeltaTime, approachTicks, 1.5f,
                            "Cú tiếp cận phải dài quãng cuộn / tốc độ.");
            Assert.AreEqual(0f, harness.Scroll, 0.01f, "Tới cổng bục thì camera ở đỉnh list.");
        }

        // ---------------------------------------------------------------- Lớp nổi của row mình

        /// <summary>
        /// Có lớp nổi (con của gốc canvas, widget bị thu 0,9 — khác cha, khác tỉ lệ với Content): suốt màn mở + cú tiếp cận view
        /// của row mình nằm trong lớp nổi và đứng ĐÚNG chỗ, đúng cỡ list sẽ vẽ nó dưới Content; host giành thanh ở PodiumTakeover
        /// thì view về lại Content, vào pool, không rò, không sinh view mới.
        /// </summary>
        [Test]
        public void FloatingLayer_CarriesTheLocalRowUnmasked_ThenReturnsItToThePool()
        {
            const float WidgetScale = 0.9f;
            Harness harness = CreateHarness(ReferenceScrollSpeed, withFloatingLayer: true, hidesLocalRowOnTakeover: true,
                                            widgetScale: WidgetScale);
            Task<LeaderboardPresentResult> task = harness.PresentRankUp(11, 1);
            LeaderboardScrollView scrollView = harness.ScrollView;
            RectTransform content = scrollView.Content;
            RowState local = harness.Model.LocalRow;
            int totalViews = 0;

            int floatingTicks = 0;
            for (int tick = 0; tick < MaximumTicks && !harness.Beats.Contains(LeaderboardBeat.PodiumTakeover); tick++)
            {
                harness.Advance(1);
                if (harness.Beats.Contains(LeaderboardBeat.PodiumTakeover)) break;
                // Pool có thể nở thêm khi list cuộn; đếm lại mỗi tick để so với đúng frame trước lúc host giành thanh.
                totalViews = CountEntryViews(content) + CountEntryViews(harness.FloatingLayer);
                LeaderboardEntryView view = scrollView.GetActiveView(local);
                Assert.IsNotNull(view, "Tick " + tick + ": row mình phải có view suốt cú tiếp cận.");
                Assert.AreSame(harness.FloatingLayer, view.transform.parent, "Tick " + tick + ": row mình không ở lớp nổi.");

                Vector3 expected = content.TransformPoint(new Vector3(content.rect.center.x + local.IntroOffsetX,
                                                                      content.rect.yMax - harness.Layout.SlotToCenter(local.Slot) -
                                                                      local.IntroOffset, 0f));
                RectTransform viewRect = view.RectTransform;
                Vector3 actual = viewRect.TransformPoint(viewRect.rect.center);
                Assert.AreEqual(expected.x, actual.x, 0.05f, "Tick " + tick + ": lớp nổi lệch chỗ list vẽ row (x).");
                Assert.AreEqual(expected.y, actual.y, 0.05f, "Tick " + tick + ": lớp nổi lệch chỗ list vẽ row (y).");
                Assert.AreEqual(content.lossyScale.x * local.Scale * local.IntroScale, viewRect.lossyScale.x, 1e-4f,
                                "Tick " + tick + ": lớp nổi đổi cỡ row.");
                floatingTicks++;
            }
            Assert.Greater(floatingTicks, 30, "Tiền đề hỏng: cú tiếp cận phải dài nhiều tick.");

            harness.Advance(1);
            Assert.IsNull(scrollView.GetActiveView(local), "Host giành thanh mà list vẫn vẽ nó.");
            foreach (LeaderboardEntryView view in harness.FloatingLayer.GetComponentsInChildren<LeaderboardEntryView>(true))
            {
                Assert.Fail("View " + view.name + " còn kẹt trong lớp nổi sau khi host giành thanh.");
            }
            Assert.AreEqual(totalViews, CountEntryViews(content), "View của row mình không về Content (pool mất view).");

            harness.Widget.ReleasePodiumHold();
            for (int tick = 0; tick < MaximumTicks && !task.IsCompleted; tick++)
            {
                harness.Advance(1);
                Assert.AreEqual(0, CountEntryViews(harness.FloatingLayer), "Tick " + tick + ": sau khi host giành thanh lớp nổi phải trống.");
            }
            Assert.IsTrue(task.IsCompleted);
        }

        /// <summary>Có lớp nổi nhưng cờ tắt: không view nào rời Content, suốt cả màn lên bục.</summary>
        [Test]
        public void FloatingLayer_FlagOff_ViewsStayUnderContent()
        {
            Harness harness = CreateHarness(0f, withFloatingLayer: true);
            Task<LeaderboardPresentResult> task = harness.PresentRankUp(9, 1);
            Assert.IsFalse(harness.Model.HasPodiumApproach);

            for (int tick = 0; tick < MaximumTicks && !task.IsCompleted; tick++)
            {
                if (harness.Timeline != null && harness.Timeline.Phase == RevealPhase.PodiumHold) harness.Widget.ReleasePodiumHold();
                harness.Advance(1);
                Assert.AreEqual(0, CountEntryViews(harness.FloatingLayer), "Tick " + tick + ": cờ tắt mà có view ở lớp nổi.");
            }
            Assert.IsTrue(task.IsCompleted);
        }
    }
}
