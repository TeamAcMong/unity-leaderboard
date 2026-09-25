using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamTech.Leaderboard.EditorTools;
using DreamTech.Leaderboard.ViewModel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DreamTech.Leaderboard.UI.Tests
{
    /// <summary>
    /// "Cờ tắt thì y như cũ" ở tầng HÌNH: dấu vân tay từng frame của widget mẫu (vị trí cuộn, số view, vị trí / cỡ / độ
    /// đục / thứ tự vẽ của từng view, banner) khi diễn các màn lên hạng vào / trong top 3, ghi từ code TRƯỚC khi có
    /// <c>MotionSettings.HostPresentedTopRanks</c>.
    ///
    /// <para>Test view-model chỉ chứng minh quỹ đạo Slot không đổi; phần mới của list (độ hiện diện, camera "đỉnh là nhà", thanh
    /// dính, tia sáng) nằm ở lớp view, nên phải có một chốt riêng ở đây.</para>
    ///
    /// <para><b>Đỏ vì bạn CỐ Ý chỉnh prefab / config mẫu của package</b> (rowHeight, padding, nhịp intro...): lấy số mới trong
    /// thông báo lỗi và ghi đè.</para>
    /// </summary>
    [TestFixture]
    public class LeaderboardFlagOffRenderTests
    {
        private const float FrameDeltaTime = 1f / 60f;
        private const int MaximumTicks = 3000;
        private const int TicksAfterFinish = 30;
        private const double Quantum = 1000.0;

        private PreviewCanvas _canvas;
        private LeaderboardWidget _widget;
        private MockLeaderboardService _service;
        private ManualScoreSource _scoreSource;
        private LeaderboardBoard _board;

        [SetUp]
        public void SetUp()
        {
            _canvas = new PreviewCanvas();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LeaderboardEditorPaths.WidgetPrefabPath).GetComponent<LeaderboardWidget>();
            _widget = _canvas.Spawn(prefab);
            _service = new MockLeaderboardService(new MockLeaderboardOptions { BotCount = 3000, LatencyMilliseconds = 0 });
            _scoreSource = new ManualScoreSource("render-golden");
            _board = new LeaderboardBoard(new LeaderboardBoardSettings("render-golden", FetchWindowSettings.Default, RankTierRule.Default),
                                          _service, _scoreSource, new InMemoryLeaderboardSnapshotStore());
        }

        [TearDown]
        public void TearDown()
        {
            _canvas.Dispose();
        }

        private static IEnumerable<TestCaseData> Goldens()
        {
            // (hạng cũ 0-based, hạng mới 0-based, dấu vân tay đã ghi từ code cũ)
            yield return new TestCaseData(9, 1, 836520241762752589UL).SetName("FlagOffRender_10To2_MatchesRecordedFrames");
            yield return new TestCaseData(2, 1, 390846703583499837UL).SetName("FlagOffRender_3To2_MatchesRecordedFrames");
            yield return new TestCaseData(5, 0, 5907703103032075581UL).SetName("FlagOffRender_6To1_MatchesRecordedFrames");
            yield return new TestCaseData(47, 36, 17867339027098721606UL).SetName("FlagOffRender_48To37_MatchesRecordedFrames");
        }

        [TestCaseSource(nameof(Goldens))]
        public void FlagOffRender_MatchesRecordedFrames(int startRank, int targetRank, ulong expected)
        {
            _service.SetLocalScore(_service.ScoreToReachRank(startRank));
            _board.MarkRevealed(RankChange.Browse(_service.GetLocalEntryAsync(CancellationToken.None).Result));
            _scoreSource.SetScore(_service.ScoreToReachRank(targetRank));

            _widget.Arm();
            Task<LeaderboardPresentResult> task = _widget.PresentAsync(new LeaderboardPresentRequest(_board, BoardPresentMode.RevealIfPending),
                                                                       CancellationToken.None);
            var values = new List<double>();
            BoardModel model = _widget.CurrentModel;
            Assert.IsNotNull(model, "Dữ liệu Mock đồng bộ nên model phải có ngay.");

            int remainingAfterFinish = TicksAfterFinish;
            for (int tick = 0; tick < MaximumTicks && remainingAfterFinish > 0; tick++)
            {
                _widget.AdvanceForTests(FrameDeltaTime);
                RecordFrame(model, values);
                if (task.IsCompleted) remainingAfterFinish--;
            }

            Assert.IsTrue(task.IsCompleted, "Trình bày phải kết thúc.");
            ulong fingerprint = Fingerprint(values);
            Assert.AreEqual(expected, fingerprint,
                            "Hình của widget khi cờ TẮT đã khác bản đã ghi (" + values.Count + " giá trị). Dấu vân tay mới: " +
                            fingerprint + "UL");
        }

        private void RecordFrame(BoardModel model, List<double> values)
        {
            LeaderboardScrollView scrollView = _widget.ScrollView;
            values.Add(scrollView.Content.anchoredPosition.y);
            values.Add(scrollView.ActiveViewCount);
            values.Add(_widget.BannerView != null && _widget.BannerView.IsVisible ? 1 : 0);

            IReadOnlyList<RowState> rows = model.Rows;
            for (int index = 0; index < rows.Count; index++)
            {
                LeaderboardEntryView view = scrollView.GetActiveView(rows[index]);
                if (view == null)
                {
                    values.Add(-1);
                    continue;
                }
                RectTransform rectTransform = view.RectTransform;
                values.Add(rectTransform.anchoredPosition.x);
                values.Add(rectTransform.anchoredPosition.y);
                values.Add(rectTransform.localScale.x);
                var group = view.GetComponent<CanvasGroup>();
                values.Add(group != null ? group.alpha : -2f);
                values.Add(ReferenceEquals(rows[index], model.LocalRow) && view.transform.GetSiblingIndex() == view.transform.parent.childCount - 1 ? 1 : 0);
            }
        }

        private static ulong Fingerprint(List<double> values)
        {
            ulong hash = 14695981039346656037UL;
            for (int index = 0; index < values.Count; index++)
            {
                long quantized = (long)Math.Round(values[index] * Quantum);
                unchecked
                {
                    for (int shift = 0; shift < 64; shift += 8)
                    {
                        hash ^= (byte)(quantized >> shift);
                        hash *= 1099511628211UL;
                    }
                }
            }
            return hash;
        }
    }
}
