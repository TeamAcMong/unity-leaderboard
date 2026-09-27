using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
    /// Phía list của <c>MotionSettings.HostPresentedTopRanks</c>: row do host trình bày không có view (kể cả row mình đang
    /// ghim), row trượt ra khỏi bục hiện dần, không rò view của pool, camera coi đỉnh list là nhà khi row mình đáp lên bục, không
    /// có thanh dính / banner / tia sáng / sao cho người đang ở trên bục, và các hàm cho host (proxy, vị trí, khung row).
    ///
    /// <para>Widget mẫu của package được bơm một bản sao config chuyển động (qua SerializedObject — đúng đường một game gán asset
    /// của nó), nên mọi thứ khác (prefab, pool, camera) là thật.</para>
    /// </summary>
    [TestFixture]
    public class LeaderboardPodiumUITests
    {
        private const float FrameDeltaTime = 1f / 60f;
        private const int MaximumTicks = 3000;
        private const int TopRanks = 3;
        private const int IntroSettleTicks = 90;

        /// <summary>
        /// <c>topPadding</c> của list trong các test camera "đỉnh là nhà": chừa một dải lớn phía trên ô 0 như một game có bục.
        ///
        /// <para>Vì sao: với bố cục mẫu (viewport ~1720 px, topPadding 52, bước 136) cuộn canh giữa của mọi ô ≤ 5 đã là 0, nên
        /// "cuộn = 0" đúng cả khi tính năng bị gỡ — test không đo được gì. Với dải này cuộn canh giữa các ô 0..3 đều &gt; 0 (xem
        /// <see cref="AssertCenteringIsNotHome"/>), giống Golden Race (canh giữa ô 2 ≈ 130, ô 3 ≈ 316).</para>
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

        /// <summary>Một widget mẫu + backend Mock + config chuyển động riêng của test.</summary>
        private sealed class Harness
        {
            public LeaderboardWidget Widget;
            public MockLeaderboardService Service;
            public ManualScoreSource ScoreSource;
            public LeaderboardBoard Board;
            public readonly List<LeaderboardBeat> Beats = new List<LeaderboardBeat>();

            public LeaderboardScrollView ScrollView => Widget.ScrollView;
            public BoardModel Model => Widget.CurrentModel;
            public RevealTimeline Timeline => Widget.CurrentTimeline;
            public float Scroll => ScrollView.Content.anchoredPosition.y;

            public void PrepareRankUp(int startRank, int targetRank)
            {
                Service.SetLocalScore(Service.ScoreToReachRank(startRank));
                Board.MarkRevealed(RankChange.Browse(Service.GetLocalEntryAsync(CancellationToken.None).Result));
                ScoreSource.SetScore(Service.ScoreToReachRank(targetRank));
            }

            public void PrepareBrowseAt(int rank)
            {
                Service.SetLocalScore(Service.ScoreToReachRank(rank));
                LeaderboardEntry entry = Service.GetLocalEntryAsync(CancellationToken.None).Result;
                Board.MarkRevealed(RankChange.Browse(entry));
                ScoreSource.SetScore(entry.Score);
            }

            public Task<LeaderboardPresentResult> Present(BoardPresentMode mode)
            {
                Widget.Arm();
                return Widget.PresentAsync(new LeaderboardPresentRequest(Board, mode), CancellationToken.None);
            }

            public void Advance(int ticks)
            {
                for (int tick = 0; tick < ticks; tick++) Widget.AdvanceForTests(FrameDeltaTime);
            }
        }

        /// <summary>Ghi nhịp; tuỳ chọn thả cổng bục ngay khi được trao quyền (host "diễn" trong 0 giây).</summary>
        private sealed class RecordingSink : ILeaderboardFeedbackSink
        {
            private readonly Harness _harness;
            private readonly bool _releasesOnTakeover;

            public RecordingSink(Harness harness, bool releasesOnTakeover)
            {
                _harness = harness;
                _releasesOnTakeover = releasesOnTakeover;
            }

            public void OnBeat(LeaderboardBeat beat, in LeaderboardBeatContext context)
            {
                _harness.Beats.Add(beat);
                if (_releasesOnTakeover && beat == LeaderboardBeat.PodiumTakeover) _harness.Widget.ReleasePodiumHold();
            }
        }

        private Harness CreateHarness(int topRanks, float followSmoothTime = -1f, float podiumClimbDuration = 0f,
                                      float podiumPassSlideDuration = 0f, float holdTimeout = 100f, bool releasesOnTakeover = false,
                                      float listTopPadding = -1f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LeaderboardEditorPaths.WidgetPrefabPath).GetComponent<LeaderboardWidget>();
            var harness = new Harness { Widget = _canvas.Spawn(prefab) };
            if (listTopPadding >= 0f)
            {
                var scrollViewObject = new SerializedObject(harness.Widget.ScrollView);
                scrollViewObject.FindProperty("topPadding").floatValue = listTopPadding;
                scrollViewObject.ApplyModifiedPropertiesWithoutUndo();
            }

            var widgetObject = new SerializedObject(harness.Widget);
            SerializedProperty motionProperty = widgetObject.FindProperty("motionConfig");
            var source = motionProperty.objectReferenceValue as LeaderboardMotionConfig;
            LeaderboardMotionConfig motion = source != null ? Object.Instantiate(source) : ScriptableObject.CreateInstance<LeaderboardMotionConfig>();
            _createdAssets.Add(motion);

            var motionObject = new SerializedObject(motion);
            motionObject.FindProperty("timeline.HostPresentedTopRanks").intValue = topRanks;
            motionObject.FindProperty("timeline.PodiumClimbDuration").floatValue = podiumClimbDuration;
            motionObject.FindProperty("timeline.PodiumPassSlideDuration").floatValue = podiumPassSlideDuration;
            motionObject.FindProperty("timeline.HostHoldTimeout").floatValue = holdTimeout;
            if (followSmoothTime >= 0f) motionObject.FindProperty("timeline.FollowSmoothTime").floatValue = followSmoothTime;
            motionObject.ApplyModifiedPropertiesWithoutUndo();
            motionProperty.objectReferenceValue = motion;
            widgetObject.ApplyModifiedPropertiesWithoutUndo();

            harness.Service = new MockLeaderboardService(new MockLeaderboardOptions { BotCount = 3000, LatencyMilliseconds = 0 });
            harness.ScoreSource = new ManualScoreSource("podium-ui");
            harness.Board = new LeaderboardBoard(new LeaderboardBoardSettings("podium-ui", FetchWindowSettings.Default, RankTierRule.Default),
                                                 harness.Service, harness.ScoreSource, new InMemoryLeaderboardSnapshotStore());
            harness.Widget.SetFeedbackSinks(new ILeaderboardFeedbackSink[] { new RecordingSink(harness, releasesOnTakeover) });
            return harness;
        }

        private static T PrivateReference<T>(Object owner, string propertyName) where T : Object
        {
            return new SerializedObject(owner).FindProperty(propertyName).objectReferenceValue as T;
        }

        /// <summary>
        /// Không rò view: mọi view ĐANG BẬT dưới Content đều là view list đang theo dõi (không view mồ côi còn vẽ), và không view
        /// nào đang vẽ một row mà host đang trình bày.
        /// </summary>
        private static void AssertViewsConsistent(Harness harness, string label, LeaderboardEntryView proxy = null)
        {
            LeaderboardScrollView scrollView = harness.ScrollView;
            int activeChildren = 0;
            foreach (Transform child in scrollView.Content)
            {
                var view = child.GetComponent<LeaderboardEntryView>();
                if (view == null || !child.gameObject.activeSelf || ReferenceEquals(view, proxy)) continue;
                activeChildren++;
            }
            Assert.AreEqual(scrollView.ActiveViewCount, activeChildren, label + ": có view đang bật mà list không theo dõi (rò pool).");

            IReadOnlyList<RowState> rows = harness.Model.Rows;
            for (int index = 0; index < rows.Count; index++)
            {
                if (harness.Model.ListPresence(rows[index]) > 0f) continue;
                Assert.IsNull(scrollView.GetActiveView(rows[index]),
                              label + ": row ô " + rows[index].Slot + " do host trình bày mà list vẫn giữ view.");
            }
        }

        private static int CountEntryViewsUnderContent(Harness harness)
        {
            return harness.ScrollView.Content.GetComponentsInChildren<LeaderboardEntryView>(true).Length;
        }

        /// <summary>
        /// Tiền đề của mọi test "camera ở nhà": cuộn CANH GIỮA các ô này phải khác 0 — không thì "cuộn = 0" cũng là kết quả của
        /// đường canh giữa cũ, và test vẫn xanh khi tính năng bị gỡ.
        /// </summary>
        private static void AssertCenteringIsNotHome(Harness harness, params int[] slots)
        {
            LeaderboardScrollView scrollView = harness.ScrollView;
            float viewportHeight = scrollView.Viewport.rect.height;
            float maximumScroll = scrollView.Layout.MaximumScroll(scrollView.Content.sizeDelta.y, viewportHeight);
            for (int index = 0; index < slots.Length; index++)
            {
                float centeredScroll = scrollView.Layout.CenteredScroll(slots[index], viewportHeight, maximumScroll);
                Assert.Greater(centeredScroll, 20f, "Tiền đề hỏng: canh giữa ô " + slots[index] +
                                                     " đã là cuộn 0 — test không phân biệt được 'đỉnh là nhà' với canh giữa.");
            }
        }

        // ---------------------------------------------------------------- Không view cho row do host trình bày

        /// <summary>Xem bảng khi mình đang hạng 2: ba ô đầu (kể cả row mình) không có view; list bắt đầu từ ô 3.</summary>
        [Test]
        public void Browse_OnPodium_HiddenRowsHaveNoViews()
        {
            Harness harness = CreateHarness(TopRanks, listTopPadding: PodiumBandTopPadding);
            harness.PrepareBrowseAt(1);
            Task<LeaderboardPresentResult> task = harness.Present(BoardPresentMode.Browse);
            harness.Advance(IntroSettleTicks);
            AssertCenteringIsNotHome(harness, 1);

            Assert.IsTrue(task.IsCompleted);
            BoardModel model = harness.Model;
            Assert.AreEqual(TopRanks, model.HiddenLeadingSlots);
            for (int slot = 0; slot < TopRanks; slot++)
            {
                Assert.IsNull(harness.ScrollView.GetActiveView(model.Rows[slot]), "Ô " + slot + " do host trình bày mà vẫn có view.");
            }
            Assert.IsNotNull(harness.ScrollView.GetActiveView(model.Rows[TopRanks]), "Ô đầu tiên của list (hạng 4) phải có view.");
            Assert.AreEqual(0f, harness.Scroll, 0.01f, "Mình đang trên bục thì xem bảng phải mở ở đỉnh list.");
            AssertViewsConsistent(harness, "browse");
        }

        /// <summary>
        /// Suốt một màn đổi chỗ trên bục (#3 → #2), row mình ĐANG GHIM mà vẫn không có view: ghim không được thắng độ hiện diện 0.
        /// Không rò view ở tick nào, số view trong pool không tăng.
        /// </summary>
        [Test]
        public void PodiumSwapReveal_PinnedLocalRowStaysWithoutView_AndPoolDoesNotGrow()
        {
            Harness harness = CreateHarness(TopRanks, releasesOnTakeover: true);
            harness.PrepareRankUp(2, 1);
            Task<LeaderboardPresentResult> task = harness.Present(BoardPresentMode.RevealIfPending);
            Assert.IsNotNull(harness.Timeline);
            Assert.IsTrue(harness.Timeline.TakesPodium);
            harness.Advance(5);
            int viewCount = CountEntryViewsUnderContent(harness);

            for (int tick = 0; tick < MaximumTicks && !task.IsCompleted; tick++)
            {
                harness.Widget.AdvanceForTests(FrameDeltaTime);
                Assert.IsNull(harness.ScrollView.GetActiveView(harness.Model.LocalRow), "Tick " + tick + ": row mình đang sau bục mà có view.");
                AssertViewsConsistent(harness, "tick " + tick);
            }

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(viewCount, CountEntryViewsUnderContent(harness), "Pool phình ra — có view bị tạo mới thay vì tái dùng.");
            Assert.AreEqual(1, CountOf(harness.Beats, LeaderboardBeat.PodiumTakeover));
        }

        /// <summary>
        /// Lên bục từ list (#10 → #2) với đoạn sau cổng có diễn: người cũ hạng 3 trượt từ sau bục ra (ô 2 → 3) — ở các frame độ
        /// hiện diện lẻ, độ đục của view đúng bằng IntroAlpha × độ hiện diện; ô trên bục không bao giờ có view; không rò view.
        /// </summary>
        [Test]
        public void PromotionReveal_RowSlidingOutFromPodium_FadesInByPresence()
        {
            Harness harness = CreateHarness(TopRanks, podiumClimbDuration: 0.4f, podiumPassSlideDuration: 0.3f, releasesOnTakeover: true);
            harness.PrepareRankUp(9, 1);
            Task<LeaderboardPresentResult> task = harness.Present(BoardPresentMode.RevealIfPending);
            int fractionalFrames = 0;

            for (int tick = 0; tick < MaximumTicks && !task.IsCompleted; tick++)
            {
                harness.Widget.AdvanceForTests(FrameDeltaTime);
                AssertViewsConsistent(harness, "tick " + tick);
                IReadOnlyList<RowState> rows = harness.Model.Rows;
                for (int index = 0; index < rows.Count; index++)
                {
                    RowState row = rows[index];
                    float presence = harness.Model.ListPresence(row);
                    if (presence <= 0f || presence >= 1f) continue;
                    LeaderboardEntryView view = harness.ScrollView.GetActiveView(row);
                    if (view == null) continue;
                    var group = view.GetComponent<CanvasGroup>();
                    Assert.IsNotNull(group, "Tiền đề hỏng: prefab row không có CanvasGroup ở gốc.");
                    Assert.AreEqual(row.IntroAlpha * presence, group.alpha, 0.0025f, "Tick " + tick + ": độ đục không theo độ hiện diện.");
                    fractionalFrames++;
                }
            }

            Assert.IsTrue(task.IsCompleted);
            Assert.Greater(fractionalFrames, 0, "Không có frame nào một row đang trượt ra khỏi bục — test không kiểm được gì.");
        }

        /// <summary>Độ hiện diện lẻ đặt tay: 0,5 → nửa độ đục; 0 → không view; về lại 1 → view quay lại, đục đầy.</summary>
        [Test]
        public void FractionalSlot_MultipliesAlpha_ZeroReleases_OneRestores()
        {
            Harness harness = CreateHarness(TopRanks);
            harness.PrepareBrowseAt(30);
            harness.Present(BoardPresentMode.Browse);
            harness.Advance(IntroSettleTicks);
            harness.ScrollView.Content.anchoredPosition = new Vector2(harness.ScrollView.Content.anchoredPosition.x, 0f);
            harness.Advance(1);

            RowState row = harness.Model.Rows[TopRanks];
            Assert.IsNotNull(harness.ScrollView.GetActiveView(row), "Tiền đề hỏng: ô 3 phải đang hiện ở đỉnh list.");

            row.Slot = TopRanks - 0.5f;
            harness.Advance(1);
            LeaderboardEntryView view = harness.ScrollView.GetActiveView(row);
            Assert.IsNotNull(view);
            Assert.AreEqual(0.5f, view.GetComponent<CanvasGroup>().alpha, 0.0025f, "Độ hiện diện 0,5 phải cho nửa độ đục.");

            row.Slot = TopRanks - 1f;
            harness.Advance(1);
            Assert.IsNull(harness.ScrollView.GetActiveView(row), "Độ hiện diện 0 phải thu hồi view.");
            AssertViewsConsistent(harness, "presence-0");

            row.Slot = TopRanks;
            harness.Advance(1);
            view = harness.ScrollView.GetActiveView(row);
            Assert.IsNotNull(view, "Về lại list thì phải có view lại.");
            Assert.AreEqual(1f, view.GetComponent<CanvasGroup>().alpha, 0.0025f);
        }

        /// <summary>
        /// Host giành thanh của mình khỏi list (<see cref="RowState.IsHiddenFromList"/>) đúng lúc đang chờ ở cổng: view (đang
        /// ghim) biến mất ngay frame sau, không bị list tạo lại frame nào, và quay lại khi host trả.
        /// </summary>
        [Test]
        public void IsHiddenFromList_ReleasesThePinnedLocalView_WithoutChurn()
        {
            Harness harness = CreateHarness(TopRanks);
            harness.PrepareRankUp(9, 1);
            Task<LeaderboardPresentResult> task = harness.Present(BoardPresentMode.RevealIfPending);
            for (int tick = 0; tick < MaximumTicks && harness.Timeline.Phase != RevealPhase.PodiumHold; tick++) harness.Advance(1);
            Assert.AreEqual(RevealPhase.PodiumHold, harness.Timeline.Phase, "Tiền đề hỏng: phải đang chờ ở cổng bục.");
            RowState local = harness.Model.LocalRow;
            Assert.IsNotNull(harness.ScrollView.GetActiveView(local), "Ở ô ranh giới thanh của mình vẫn phải hiện.");
            int viewCount = CountEntryViewsUnderContent(harness);

            local.IsHiddenFromList = true;
            for (int tick = 0; tick < 30; tick++)
            {
                harness.Advance(1);
                Assert.IsNull(harness.ScrollView.GetActiveView(local), "Tick " + tick + ": host đã giành thanh mà list vẫn vẽ nó.");
                AssertViewsConsistent(harness, "hidden tick " + tick);
            }
            Assert.AreEqual(viewCount, CountEntryViewsUnderContent(harness), "Ẩn / hiện không được sinh view mới.");

            local.IsHiddenFromList = false;
            harness.Advance(1);
            Assert.IsNotNull(harness.ScrollView.GetActiveView(local), "Host trả thanh thì list phải vẽ lại.");

            harness.Widget.ReleasePodiumHold();
            for (int tick = 0; tick < MaximumTicks && !task.IsCompleted; tick++) harness.Advance(1);
            Assert.AreEqual(PresentOutcome.Completed, task.Result.Outcome);
        }

        // ---------------------------------------------------------------- Camera "đỉnh là nhà"

        /// <summary>
        /// Bám neo (FollowSmoothTime 0): leo từ #15 lên #2 — camera trôi từ chỗ canh giữa row mình về ĐÚNG 0 lúc row chạm ranh
        /// giới, đứng yên ở 0 suốt lúc chờ, lúc đi nốt và lúc đáp.
        /// </summary>
        [Test]
        public void AnchoredFollow_ReachesScrollZeroAtThePodiumHold_AndStaysHome()
        {
            Harness harness = CreateHarness(TopRanks, followSmoothTime: 0f, listTopPadding: PodiumBandTopPadding);
            harness.PrepareRankUp(14, 1);
            Task<LeaderboardPresentResult> task = harness.Present(BoardPresentMode.RevealIfPending);
            harness.Advance(1);
            Assert.Greater(harness.Scroll, 1f, "Tiền đề hỏng: lúc bắt đầu camera phải đang canh giữa row mình ở dưới.");
            AssertCenteringIsNotHome(harness, 1, 2, 3);

            for (int tick = 0; tick < MaximumTicks && harness.Timeline.Phase != RevealPhase.PodiumHold; tick++) harness.Advance(1);
            Assert.AreEqual(RevealPhase.PodiumHold, harness.Timeline.Phase);
            Assert.AreEqual(0f, harness.Scroll, 0.01f, "Chạm ranh giới mà camera chưa về đỉnh list — bục bị cắt.");

            harness.Advance(30);
            Assert.AreEqual(0f, harness.Scroll, 0.01f, "Camera trôi khỏi đỉnh list trong lúc chờ host.");

            harness.Widget.ReleasePodiumHold();
            for (int tick = 0; tick < MaximumTicks && !task.IsCompleted; tick++)
            {
                harness.Advance(1);
                Assert.AreEqual(0f, harness.Scroll, 0.01f, "Tick " + tick + ": camera rời đỉnh list sau khi thả cổng.");
            }
            Assert.IsTrue(task.IsCompleted);
        }

        /// <summary>Bám mềm (FollowSmoothTime &gt; 0) cũng phải về nhà — chậm hơn một nhịp, nhưng về tới 0 trong lúc chờ host.</summary>
        [Test]
        public void SmoothFollow_SettlesAtScrollZeroDuringThePodiumHold()
        {
            Harness harness = CreateHarness(TopRanks, followSmoothTime: 0.12f, listTopPadding: PodiumBandTopPadding);
            harness.PrepareRankUp(14, 1);
            harness.Present(BoardPresentMode.RevealIfPending);
            harness.Advance(1);
            Assert.Greater(harness.Scroll, 1f, "Tiền đề hỏng: lúc bắt đầu camera phải đang ở dưới.");
            AssertCenteringIsNotHome(harness, 1, 2, 3);

            for (int tick = 0; tick < MaximumTicks && harness.Timeline.Phase != RevealPhase.PodiumHold; tick++) harness.Advance(1);
            harness.Advance(60);

            Assert.AreEqual(RevealPhase.PodiumHold, harness.Timeline.Phase);
            Assert.AreEqual(0f, harness.Scroll, 0.5f, "Bám mềm không đưa camera về đỉnh list khi row mình lên bục.");
        }

        /// <summary>
        /// Bắt đầu ở ranh giới (#4 → #3) hoặc đã trên bục (#3 → #2): camera ở nhà từ frame đầu và không nhúc nhích suốt màn diễn.
        /// </summary>
        [TestCase(3, 2, TestName = "BoundaryStart_CameraStaysAtTop")]
        [TestCase(2, 1, TestName = "PodiumSwap_CameraStaysAtTop")]
        public void StartAtOrAbovePodiumBoundary_CameraStaysAtTop(int startRank, int targetRank)
        {
            Harness harness = CreateHarness(TopRanks, followSmoothTime: 0f, releasesOnTakeover: true, listTopPadding: PodiumBandTopPadding);
            harness.PrepareRankUp(startRank, targetRank);
            Task<LeaderboardPresentResult> task = harness.Present(BoardPresentMode.RevealIfPending);
            AssertCenteringIsNotHome(harness, startRank, targetRank);
            Assert.AreEqual(0f, harness.Scroll, 0.01f, "SetModel phải đặt camera ở đỉnh list.");

            for (int tick = 0; tick < MaximumTicks && !task.IsCompleted; tick++)
            {
                harness.Advance(1);
                Assert.AreEqual(0f, harness.Scroll, 0.01f, "Tick " + tick + ": camera rời đỉnh list.");
            }
            Assert.IsTrue(task.IsCompleted);
        }

        /// <summary>Row mình trên bục: bấm "cuộn tới mình" và yêu cầu snap đều về đỉnh list, không canh giữa một ô sau bục.</summary>
        [Test]
        public void ScrollToLocalRow_AndSnap_GoHomeWhenLocalIsOnPodium()
        {
            Harness harness = CreateHarness(TopRanks, listTopPadding: PodiumBandTopPadding);
            harness.PrepareBrowseAt(1);
            harness.Present(BoardPresentMode.Browse);
            harness.Advance(IntroSettleTicks);
            AssertCenteringIsNotHome(harness, 1);
            RectTransform content = harness.ScrollView.Content;

            content.anchoredPosition = new Vector2(content.anchoredPosition.x, 1500f);
            harness.ScrollView.ScrollToLocalRow();
            harness.Advance(IntroSettleTicks);
            Assert.AreEqual(0f, harness.Scroll, 0.01f, "Cuộn tới mình (đang trên bục) phải về đỉnh list.");

            content.anchoredPosition = new Vector2(content.anchoredPosition.x, 1500f);
            harness.ScrollView.RequestSnapToLocalRow();
            harness.Advance(1);
            Assert.AreEqual(0f, harness.Scroll, 0.01f, "Snap tới mình (đang trên bục) phải về đỉnh list.");
        }

        // ---------------------------------------------------------------- Mở ở đỉnh list (OpenAtTop)

        /// <summary>
        /// <see cref="LeaderboardScrollView.OpenAtTop"/>: bật thì xem bảng khi mình ở hạng 30 mở ở ĐỈNH list và hàng đầu tiên của list
        /// (hạng 4) có view + đang trượt vào; tắt (mặc định) thì y như trước — canh vào row mình, không ở đỉnh.
        /// </summary>
        [TestCase(true, TestName = "Browse_OpenAtTop_StartsAtTopAndSlidesFirstListedRow")]
        [TestCase(false, TestName = "Browse_OpenAtTopOff_CentersLocalRowAsBefore")]
        public void Browse_OpenAtTop(bool opensAtTop)
        {
            Harness harness = CreateHarness(TopRanks, listTopPadding: PodiumBandTopPadding);
            harness.ScrollView.OpenAtTop = opensAtTop;
            harness.PrepareBrowseAt(30);
            harness.Present(BoardPresentMode.Browse);

            BoardModel model = harness.Model;
            Assert.IsNotNull(model, "Browse với dữ liệu cục bộ phải dựng model ngay trong PresentAsync.");
            if (!opensAtTop)
            {
                Assert.Greater(harness.Scroll, 20f, "Cờ tắt: xem bảng phải canh vào row mình như trước.");
                return;
            }

            Assert.AreEqual(0f, harness.Scroll, 0.01f, "OpenAtTop: xem bảng phải mở ở đỉnh list.");
            RowState firstListed = model.Rows[TopRanks];
            Assert.IsNotNull(harness.ScrollView.GetActiveView(firstListed), "Hàng đầu của list (hạng 4) phải có view ở khung đầu.");
            Assert.IsTrue(firstListed.IsIntroPlaying, "Đợt trượt vào phải tính theo chỗ cuộn 0 — hàng đầu list phải đang trượt.");
            harness.Advance(IntroSettleTicks);
            Assert.AreEqual(0f, harness.Scroll, 0.01f, "Camera không được tự trôi về row mình sau khi mở ở đỉnh.");
        }

        // ---------------------------------------------------------------- Thanh dính, banner, tia sáng, sao

        /// <summary>
        /// Mình đang trên bục, cuộn xa xuống dưới: KHÔNG có thanh "hạng của bạn" dính mép. Cờ tắt thì cùng thao tác đó có thanh
        /// dính — để chắc test đo đúng thứ nó định đo.
        /// </summary>
        [TestCase(TopRanks, false, TestName = "Sticky_LocalOnPodium_NeverShows")]
        [TestCase(0, true, TestName = "Sticky_FlagOff_ShowsAsBefore")]
        public void Sticky_WhenLocalRowIsOnPodium(int topRanks, bool expectsSticky)
        {
            Harness harness = CreateHarness(topRanks);
            harness.PrepareBrowseAt(1);
            harness.Present(BoardPresentMode.Browse);
            harness.Advance(IntroSettleTicks);
            var stickyAnchor = PrivateReference<RectTransform>(harness.ScrollView, "stickyAnchor");
            Assert.IsNotNull(stickyAnchor, "Tiền đề hỏng: widget mẫu phải có thanh dính.");

            RectTransform content = harness.ScrollView.Content;
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, 2000f);
            bool wasShown = false;
            for (int tick = 0; tick < 30; tick++)
            {
                harness.Advance(1);
                wasShown |= stickyAnchor.gameObject.activeSelf;
            }

            Assert.AreEqual(expectsSticky, wasShown);
        }

        /// <summary>
        /// Đáp lên bục (#10 → #2): tia sáng, banner, sao, confetti neo theo thanh list đều không có (host ăn mừng trên cờ);
        /// nhịp Land và Celebrate vẫn phát cho sink. Cờ tắt thì cùng màn đó có đủ — để chắc test đo đúng thứ nó định đo.
        /// </summary>
        [TestCase(TopRanks, false, TestName = "PodiumLanding_HasNoListAnchoredCelebration")]
        [TestCase(0, true, TestName = "FlagOffLanding_StillCelebratesOnTheRow")]
        public void Landing_ListAnchoredCelebration(int topRanks, bool expectsCelebration)
        {
            Harness harness = CreateHarness(topRanks, releasesOnTakeover: true);
            harness.PrepareRankUp(9, 1);
            Task<LeaderboardPresentResult> task = harness.Present(BoardPresentMode.RevealIfPending);
            var sunburst = PrivateReference<RankSunburstView>(harness.ScrollView, "sunburst");
            Assert.IsNotNull(harness.Widget.BannerView, "Tiền đề hỏng: widget mẫu phải có banner.");
            Assert.IsNotNull(sunburst, "Tiền đề hỏng: widget mẫu phải có tia sáng.");
            Assert.IsNotNull(harness.Widget.CelebrationView, "Tiền đề hỏng: widget mẫu phải có lớp hạt.");

            bool bannerShown = false;
            bool sunburstPlayed = false;
            for (int tick = 0; tick < MaximumTicks && !task.IsCompleted; tick++)
            {
                harness.Advance(1);
                bannerShown |= harness.Widget.BannerView.IsVisible;
                sunburstPlayed |= sunburst.IsPlaying;
            }

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(expectsCelebration, bannerShown, "Banner");
            Assert.AreEqual(expectsCelebration, sunburstPlayed, "Tia sáng");
            Assert.AreEqual(expectsCelebration, harness.Widget.CelebrationView.AliveCount > 0, "Sao / confetti");
            Assert.AreEqual(1, CountOf(harness.Beats, LeaderboardBeat.Land), "Nhịp Land vẫn phải phát.");
            Assert.AreEqual(1, CountOf(harness.Beats, LeaderboardBeat.Celebrate), "Nhịp Celebrate vẫn phải phát.");
        }

        // ---------------------------------------------------------------- Hàm cho host

        /// <summary>
        /// Proxy: nằm dưới parent, bind đúng row, độ đục đầy, cỡ theo dáng row (đứng yên = 1), đứng ĐÚNG chỗ list đặt row đó; list
        /// không đụng vào nó (không
        /// thu hồi, không dời) và nó không làm lệch sổ sách của pool; huỷ được, và không huỷ nhầm view của list.
        /// </summary>
        [Test]
        public void RowProxy_CreateAndDestroy_LeavesThePoolAlone()
        {
            Harness harness = CreateHarness(TopRanks);
            harness.PrepareBrowseAt(20);
            harness.Present(BoardPresentMode.Browse);
            harness.Advance(IntroSettleTicks);
            LeaderboardScrollView scrollView = harness.ScrollView;
            RowState local = harness.Model.LocalRow;
            LeaderboardEntryView listView = scrollView.GetActiveView(local);
            Assert.IsNotNull(listView, "Tiền đề hỏng: thanh của mình phải đang hiện.");
            AssertSamePosition(scrollView.GetRowAnchoredPosition(local.Slot), listView.RectTransform.anchoredPosition,
                               "GetRowAnchoredPosition phải trùng chỗ list đặt một row đứng yên.");
            int activeViewCount = scrollView.ActiveViewCount;

            LeaderboardEntryView proxy = scrollView.CreateRowProxy(local, scrollView.Content);

            Assert.IsNotNull(proxy);
            Assert.AreNotSame(listView, proxy);
            Assert.AreSame(scrollView.Content, proxy.transform.parent);
            Assert.IsTrue(proxy.gameObject.activeSelf);
            Assert.AreSame(local, proxy.BoundRow);
            Assert.AreEqual(1f, proxy.GetComponent<CanvasGroup>().alpha, 0.001f);
            Assert.AreEqual(Vector3.one, proxy.RectTransform.localScale, "Row đứng yên (không nhấc, hết intro) thì proxy cỡ 1.");
            AssertSamePosition(listView.RectTransform.anchoredPosition, proxy.RectTransform.anchoredPosition, "Proxy phải đè khít thanh thật.");
            Assert.AreEqual(activeViewCount, scrollView.ActiveViewCount, "Proxy không được tính vào view của list.");

            var hostPosition = new Vector2(123f, -456f);
            proxy.RectTransform.anchoredPosition = hostPosition;
            harness.Advance(30);
            Assert.IsTrue(proxy != null && proxy.gameObject.activeSelf, "List đã thu hồi / tắt proxy của host.");
            Assert.AreEqual(hostPosition, proxy.RectTransform.anchoredPosition, "List đã dời proxy của host.");
            AssertViewsConsistent(harness, "with-proxy", proxy);

            scrollView.DestroyRowProxy(listView);
            harness.Advance(1);
            Assert.IsTrue(listView != null, "DestroyRowProxy đã huỷ một view của list.");
            Assert.AreSame(listView, scrollView.GetActiveView(local));

            scrollView.DestroyRowProxy(proxy);
            Assert.IsTrue(proxy == null, "Proxy phải bị huỷ.");
            Assert.DoesNotThrow(() => scrollView.DestroyRowProxy(null));
            Assert.IsNull(scrollView.CreateRowProxy(null, scrollView.Content));
            Assert.IsNull(scrollView.CreateRowProxy(local, null));
            AssertViewsConsistent(harness, "after-destroy");
        }

        /// <summary>
        /// Proxy tạo đúng lúc host nhận quyền (<c>PodiumTakeover</c> — row mình còn dáng nhấc, <c>Scale</c> = <c>LiftScale</c>)
        /// phải trùng khít thanh list: cùng cỡ, cùng vị trí. Trước đây proxy luôn cỡ 1, nên host giành thanh (IsHiddenFromList) và
        /// thay bằng proxy trong cùng frame là thanh co lại ~5 % đúng khung đầu của cú bay.
        /// </summary>
        [Test]
        public void RowProxy_AtPodiumTakeover_MatchesTheLiftedListBar()
        {
            Harness harness = CreateHarness(TopRanks);
            harness.PrepareRankUp(9, 1);
            Task<LeaderboardPresentResult> task = harness.Present(BoardPresentMode.RevealIfPending);
            for (int tick = 0; tick < MaximumTicks && harness.Timeline.Phase != RevealPhase.PodiumHold; tick++) harness.Advance(1);
            Assert.AreEqual(RevealPhase.PodiumHold, harness.Timeline.Phase, "Tiền đề hỏng: phải đang chờ ở cổng bục.");
            LeaderboardScrollView scrollView = harness.ScrollView;
            RowState local = harness.Model.LocalRow;
            LeaderboardEntryView listView = scrollView.GetActiveView(local);
            Assert.IsNotNull(listView, "Tiền đề hỏng: thanh của mình phải đang hiện ở ô ranh giới.");
            Vector3 listScale = listView.RectTransform.localScale;
            Vector2 listPosition = listView.RectTransform.anchoredPosition;
            Assert.Greater(listScale.x, 1.01f, "Tiền đề hỏng: lúc nhận quyền row mình phải còn dáng nhấc.");

            // Đúng trình tự host làm trong một frame: tạo proxy, giành thanh khỏi list.
            LeaderboardEntryView proxy = scrollView.CreateRowProxy(local, scrollView.Content);
            local.IsHiddenFromList = true;

            Assert.AreEqual(listScale.x, proxy.RectTransform.localScale.x, 1e-4f,
                            "Proxy phải cùng cỡ với thanh list lúc bàn giao (row.Scale × IntroScale), không phải cỡ 1.");
            Assert.AreEqual(listScale.y, proxy.RectTransform.localScale.y, 1e-4f);
            AssertSamePosition(listPosition, proxy.RectTransform.anchoredPosition, "Proxy phải đè khít thanh list.");

            harness.Advance(1);
            Assert.IsNull(scrollView.GetActiveView(local), "Host đã giành thanh mà list vẫn vẽ nó.");
            Assert.AreEqual(listScale.x, proxy.RectTransform.localScale.x, 1e-4f, "List không được đụng vào proxy của host.");

            scrollView.DestroyRowProxy(proxy);
            local.IsHiddenFromList = false;
            harness.Widget.ReleasePodiumHold();
            for (int tick = 0; tick < MaximumTicks && !task.IsCompleted; tick++) harness.Advance(1);
            Assert.IsTrue(task.IsCompleted);
        }

        /// <summary>Row sau bục không có view nhưng khung của nó vẫn tính được từ slot — host dùng làm điểm bay.</summary>
        [Test]
        public void TryGetRowBounds_WorksForHiddenRows()
        {
            Harness harness = CreateHarness(TopRanks);
            harness.PrepareBrowseAt(1);
            harness.Present(BoardPresentMode.Browse);
            harness.Advance(IntroSettleTicks);
            LeaderboardScrollView scrollView = harness.ScrollView;
            RowState local = harness.Model.LocalRow;
            Assert.IsNull(scrollView.GetActiveView(local), "Tiền đề hỏng: row mình phải đang sau bục.");
            Assert.AreEqual(0f, scrollView.ListPresence(local));

            Assert.IsTrue(scrollView.TryGetRowBounds(local, scrollView.Content, out Rect bounds));
            Assert.AreEqual(scrollView.Content.rect.yMax + scrollView.GetRowAnchoredPosition(local.Slot).y, bounds.center.y, 0.01f);
            Assert.AreEqual(scrollView.RowHeight, bounds.height, 0.01f);
        }

        // ---------------------------------------------------------------- Cổng bục ở widget

        /// <summary>Host không bao giờ thả cổng bục: màn diễn vẫn xong và widget KÊU (cảnh báo nêu tên ReleasePodiumHold).</summary>
        [Test]
        public void PodiumHoldTimeout_FinishesAndWarns()
        {
            Harness harness = CreateHarness(TopRanks, holdTimeout: 0.3f);
            harness.PrepareRankUp(9, 1);
            LogAssert.Expect(LogType.Warning, new Regex("ReleasePodiumHold"));

            Task<LeaderboardPresentResult> task = harness.Present(BoardPresentMode.RevealIfPending);
            for (int tick = 0; tick < MaximumTicks && !task.IsCompleted; tick++) harness.Advance(1);

            Assert.IsTrue(task.IsCompleted, "Hết giờ ở cổng bục mà màn diễn vẫn treo.");
            Assert.AreEqual(PresentOutcome.Completed, task.Result.Outcome);
            Assert.IsFalse(harness.Board.HasUnrevealedChange);
        }

        /// <summary>Gọi thả cổng bục khi chưa / không có màn diễn là vô hại (host gọi từ mọi đường thoát).</summary>
        [Test]
        public void ReleasePodiumHold_WithoutReveal_IsHarmless()
        {
            Harness harness = CreateHarness(TopRanks);
            Assert.DoesNotThrow(() => harness.Widget.ReleasePodiumHold());

            harness.PrepareBrowseAt(40);
            harness.Present(BoardPresentMode.Browse);
            Assert.DoesNotThrow(() => harness.Widget.ReleasePodiumHold());
        }

        /// <summary>Bỏ qua giữa lúc chờ ở cổng bục: kết thúc Skipped, đã ghi nhận xem, camera ở nhà.</summary>
        [Test]
        public void SkipDuringPodiumHold_FinishesAtHome()
        {
            Harness harness = CreateHarness(TopRanks, followSmoothTime: 0f, listTopPadding: PodiumBandTopPadding);
            harness.PrepareRankUp(9, 1);
            Task<LeaderboardPresentResult> task = harness.Present(BoardPresentMode.RevealIfPending);
            AssertCenteringIsNotHome(harness, 1);
            for (int tick = 0; tick < MaximumTicks && harness.Timeline.Phase != RevealPhase.PodiumHold; tick++) harness.Advance(1);
            Assert.AreEqual(RevealPhase.PodiumHold, harness.Timeline.Phase);

            harness.Widget.Skip();
            for (int tick = 0; tick < MaximumTicks && !task.IsCompleted; tick++) harness.Advance(1);

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(PresentOutcome.Skipped, task.Result.Outcome);
            Assert.IsFalse(harness.Board.HasUnrevealedChange);
            Assert.AreEqual(0f, harness.Scroll, 0.01f);
            AssertViewsConsistent(harness, "after-skip");
        }

        // ---------------------------------------------------------------- Cờ bật, màn lên hạng thường: hình y hệt

        /// <summary>
        /// Bật cờ mà màn lên hạng không đáp vào bục (#48 → #37): mọi frame (vị trí cuộn, từng view: vị trí / cỡ / độ đục) trùng
        /// khít khi cờ tắt. Đây là điều kiện để Golden Race bật cờ mà các màn lên hạng thường đã chỉnh từng khung không đổi.
        /// </summary>
        [Test]
        public void FlagOn_NonPodiumRankUp_RendersIdenticallyToFlagOff()
        {
            List<double> flagOff = RecordReveal(CreateHarness(0), 47, 36);
            List<double> flagOn = RecordReveal(CreateHarness(TopRanks), 47, 36);

            Assert.Greater(flagOff.Count, 100);
            CollectionAssert.AreEqual(flagOff, flagOn, "Hình của một màn lên hạng thường khác đi khi bật cờ bục.");
        }

        private static List<double> RecordReveal(Harness harness, int startRank, int targetRank)
        {
            harness.PrepareRankUp(startRank, targetRank);
            Task<LeaderboardPresentResult> task = harness.Present(BoardPresentMode.RevealIfPending);
            var values = new List<double>();
            for (int tick = 0; tick < MaximumTicks && !task.IsCompleted; tick++)
            {
                harness.Advance(1);
                values.Add(harness.Scroll);
                values.Add(harness.ScrollView.ActiveViewCount);
                IReadOnlyList<RowState> rows = harness.Model.Rows;
                for (int index = 0; index < rows.Count; index++)
                {
                    LeaderboardEntryView view = harness.ScrollView.GetActiveView(rows[index]);
                    if (view == null)
                    {
                        values.Add(-1);
                        continue;
                    }
                    values.Add(view.RectTransform.anchoredPosition.x);
                    values.Add(view.RectTransform.anchoredPosition.y);
                    values.Add(view.RectTransform.localScale.x);
                    values.Add(view.GetComponent<CanvasGroup>().alpha);
                }
            }
            Assert.IsTrue(task.IsCompleted);
            return values;
        }

        /// <summary>
        /// So vị trí có dung sai: view chỉ ghi anchoredPosition khi lệch quá 0,01 px (cache chống ghi thừa), nên cuối intro nó có
        /// thể đứng lệch chỗ chính xác một phần nhỏ hơn thế.
        /// </summary>
        private static void AssertSamePosition(Vector2 expected, Vector2 actual, string message)
        {
            Assert.AreEqual(expected.x, actual.x, 0.02f, message);
            Assert.AreEqual(expected.y, actual.y, 0.02f, message);
        }

        private static int CountOf(List<LeaderboardBeat> beats, LeaderboardBeat beat)
        {
            int count = 0;
            for (int index = 0; index < beats.Count; index++)
            {
                if (beats[index] == beat) count++;
            }
            return count;
        }
    }
}
