using DreamTech.Leaderboard.ViewModel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DreamTech.Leaderboard.UI.Tests
{
    /// <summary>
    /// <see cref="LeaderboardRowRankUpStream"/> là hàm thuần của (mốc dòng trên RowState, đồng hồ): cùng đầu vào thì
    /// cùng số mũi tên, dù gọi theo thứ tự nào — đó là thứ giữ nó đúng qua thu hồi view và skip.
    /// </summary>
    [TestFixture]
    public class LeaderboardRowRankUpStreamTests
    {
        private GameObject _root;
        private LeaderboardRowRankUpStream _stream;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Stream", typeof(RectTransform));
            ((RectTransform)_root.transform).sizeDelta = new Vector2(900f, 180f);
            var arrow = new GameObject("Arrow", typeof(RectTransform), typeof(Image));
            arrow.transform.SetParent(_root.transform, false);
            arrow.SetActive(false);
            _stream = _root.AddComponent<LeaderboardRowRankUpStream>();
            var serialized = new UnityEditor.SerializedObject(_stream);
            serialized.FindProperty("arrowTemplate").objectReferenceValue = arrow.GetComponent<Image>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        private static RowState LocalRow()
        {
            return new RowState(BoardRow.ForEntry(new LeaderboardEntry("me", "You", 15, 36), true), 0f);
        }

        [Test]
        public void NoStream_DrawsNothing()
        {
            _stream.Render(LocalRow(), 10.0);
            Assert.AreEqual(0, _stream.VisibleCount);
        }

        [Test]
        public void OtherPlayersRow_DrawsNothing()
        {
            var other = new RowState(BoardRow.ForEntry(new LeaderboardEntry("other", "Other", 20, 30), false), 1f);
            other.StartRankUpStream(1.0);
            _stream.Render(other, 1.2);
            Assert.AreEqual(0, _stream.VisibleCount);
        }

        /// <summary>Clip: hai mũi bung ngay ở cú lật, mũi thường đầu tiên chỉ đến sau ~0,12 s.</summary>
        [Test]
        public void StreamStart_ShowsOpeningBurstThenRegularArrows()
        {
            RowState row = LocalRow();
            row.StartRankUpStream(1.0);

            _stream.Render(row, 1.0);
            Assert.AreEqual(2, _stream.VisibleCount, "Cú lật phải bung đúng hai mũi.");

            _stream.Render(row, 1.2);
            Assert.AreEqual(3, _stream.VisibleCount, "Sau ~0,2 s phải có thêm mũi thường đầu tiên.");
        }

        [Test]
        public void SameClock_SameArrows_RegardlessOfCallOrder()
        {
            RowState row = LocalRow();
            row.StartRankUpStream(0.0);
            _stream.Render(row, 0.9);
            int direct = _stream.VisibleCount;
            _stream.Render(row, 0.1);
            _stream.Render(row, 2.5);
            _stream.Render(row, 0.9);
            Assert.AreEqual(direct, _stream.VisibleCount);
        }

        /// <summary>Row đáp: thôi sinh mũi mới, mũi còn lại tắt hẳn sau stopFadeDuration (0,18 s).</summary>
        [Test]
        public void AfterEnd_ArrowsFadeOutCompletely()
        {
            RowState row = LocalRow();
            row.StartRankUpStream(0.0);
            row.EndRankUpStream(1.0);

            _stream.Render(row, 1.05);
            Assert.Greater(_stream.VisibleCount, 0, "Ngay sau khi đáp, mũi đang bay phải còn (đang tắt dần).");

            _stream.Render(row, 1.25);
            Assert.AreEqual(0, _stream.VisibleCount);
        }

        /// <summary>stopFadeDelay: mũi đang bay giữ nguyên độ sáng một nhịp sau khi đáp rồi mới tắt (clip: còn rõ tới +0,12 s).</summary>
        [Test]
        public void StopFadeDelay_HoldsArrowsThenFades()
        {
            var serialized = new UnityEditor.SerializedObject(_stream);
            serialized.FindProperty("stopFadeDelay").floatValue = 0.12f;
            serialized.FindProperty("stopFadeDuration").floatValue = 0.08f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            RowState row = LocalRow();
            row.StartRankUpStream(0.0);
            row.EndRankUpStream(1.0);
            _stream.Render(row, 1.1);
            Assert.Greater(_stream.VisibleCount, 0, "Trong nhịp giữ mà mũi tên đã tắt.");
            _stream.Render(row, 1.21);
            Assert.AreEqual(0, _stream.VisibleCount);
        }

        [Test]
        public void PoolIsBounded()
        {
            RowState row = LocalRow();
            row.StartRankUpStream(0.0);
            for (double clock = 0.0; clock < 5.0; clock += 1.0 / 60.0) _stream.Render(row, clock);
            Assert.LessOrEqual(_stream.PoolCount, 8);
            Assert.LessOrEqual(_stream.VisibleCount, _stream.PoolCount);
        }
    }
}
