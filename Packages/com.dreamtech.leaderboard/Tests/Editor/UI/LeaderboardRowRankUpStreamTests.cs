using System.Globalization;
using System.Text;
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

        // ── Kiểu hệ hạt (0.5.0, opt-in) ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Dấu vân tay của cấu hình MẶC ĐỊNH, ghi từ bản 0.4.0 (trước khi có bốn field kiểu hệ hạt): vị trí, cỡ, độ nở và độ
        /// đục của từng mũi ở 20 mốc đồng hồ, cả trước lúc bắt đầu, suốt dòng và sau khi dừng. KHÔNG ghi lại chuỗi này —
        /// lệch một chữ số là field mới đã rò vào đường mặc định.
        /// </summary>
        private const string DefaultsFingerprint040 =
            "0.90:|" +
            "1.00:-316.80,24.99,48.88,1.000,1.000;172.68,9.08,43.00,1.000,1.000;|" +
            "1.05:-316.80,33.66,48.88,1.000,1.000;172.68,16.91,43.00,1.000,1.000;|" +
            "1.13:-316.80,48.40,48.88,1.000,1.000;172.68,30.32,43.00,1.000,1.000;-129.83,-20.46,61.11,0.737,0.189;|" +
            "1.20:-316.80,62.19,48.88,1.000,0.657;172.68,42.93,43.00,1.000,1.000;-129.83,-12.00,61.11,1.000,1.000;|" +
            "1.31:-316.80,85.54,48.88,1.000,0.101;172.68,64.44,43.00,1.000,0.601;-129.83,2.98,61.11,1.000,1.000;|" +
            "1.47:-129.83,28.41,61.11,1.000,1.000;359.65,-2.70,55.23,1.000,1.000;|" +
            "1.50:-129.83,33.66,61.11,1.000,1.000;359.65,1.53,55.23,1.000,1.000;|" +
            "1.66:-129.83,64.23,61.11,1.000,0.606;359.65,26.69,55.23,1.000,1.000;57.13,-4.08,49.35,1.000,1.000;|" +
            "1.80:359.65,52.26,55.23,1.000,0.909;57.13,16.75,49.35,1.000,1.000;-245.39,-12.00,43.47,1.000,1.000;|" +
            "1.93:359.65,78.97,55.23,1.000,0.253;57.13,39.06,49.35,1.000,1.000;-245.39,5.92,43.47,1.000,1.000;244.10,-20.46,61.58,0.737,0.189;|" +
            "2.00:57.13,52.26,49.35,1.000,0.909;-245.39,16.75,43.47,1.000,1.000;244.10,-12.00,61.58,1.000,1.000;|" +
            "2.21:-245.39,54.21,43.47,1.000,0.859;244.10,18.36,61.58,1.000,1.000;-58.42,-10.72,55.70,1.000,1.000;|" +
            "2.40:244.10,52.26,61.58,1.000,0.909;-58.42,16.75,55.70,1.000,1.000;-360.94,-12.00,49.82,1.000,1.000;|" +
            "2.55:244.10,83.33,61.58,1.000,0.152;-58.42,42.75,55.70,1.000,1.000;-360.94,8.93,49.82,1.000,1.000;128.55,-18.13,43.93,0.925,0.568;|" +
            "2.77:-58.42,87.76,55.70,1.000,0.051;-360.94,46.50,49.82,1.000,1.000;128.55,12.01,43.93,1.000,1.000;-173.97,-15.72,62.05,0.999,0.947;|" +
            "2.99:128.55,50.32,43.93,1.000,0.960;-173.97,15.15,62.05,1.000,1.000;315.51,-13.26,56.17,1.000,1.000;|" +
            "3.02:128.55,56.18,43.93,1.000,0.718;-173.97,20.00,62.05,1.000,0.889;315.51,-9.43,56.17,1.000,0.889;|" +
            "3.10:128.55,72.55,43.93,1.000,0.180;-173.97,33.66,62.05,1.000,0.444;315.51,1.53,56.17,1.000,0.444;|" +
            "3.20:|";

        [Test]
        public void Defaults_RenderExactlyLikeVersion040()
        {
            RowState row = LocalRow();
            row.StartRankUpStream(1.0);
            row.EndRankUpStream(3.0);
            double[] clocks = { 0.9, 1.0, 1.05, 1.13, 1.2, 1.31, 1.47, 1.5, 1.66, 1.8, 1.93, 2.0, 2.21, 2.4, 2.55, 2.77, 2.99, 3.02, 3.1, 3.2 };
            var fingerprint = new StringBuilder();
            foreach (double clock in clocks)
            {
                _stream.Render(row, clock);
                fingerprint.Append(clock.ToString("0.00", CultureInfo.InvariantCulture)).Append(':');
                AppendVisibleArrows(fingerprint);
                fingerprint.Append('|');
            }
            Assert.AreEqual(DefaultsFingerprint040, fingerprint.ToString());
        }

        [Test]
        public void LifetimeRange_EachArrowGetsItsOwnDeterministicLifetime()
        {
            SetParticleFields(new Vector2(0.5f, 1f), Vector2.zero, 0f);
            float shortest = float.MaxValue, longest = float.MinValue;
            for (int sequence = 0; sequence < 40; sequence++)
            {
                float value = _stream.LifetimeOf(sequence);
                Assert.That(value, Is.InRange(0.5f, 1f), "mũi " + sequence);
                Assert.AreEqual(value, _stream.LifetimeOf(sequence), "Cùng k phải cùng tuổi thọ (hàm thuần).");
                shortest = Mathf.Min(shortest, value);
                longest = Mathf.Max(longest, value);
            }
            Assert.Greater(longest - shortest, 0.3f, "Dãy tuổi thọ phải trải gần hết khoảng, không dồn một chỗ.");
        }

        /// <summary>
        /// Prewarm 1 s với nhịp 0,2 s: năm mũi sinh trước mốc bắt đầu, mũi sống 0,5–1,0 s ⇒ lúc bắt đầu còn thấy 3–4 mũi
        /// (kỳ vọng của một hệ hạt 5 hạt/giây prewarm một chu kỳ: ≈ 3,2–3,75). Opening burst bị bỏ qua.
        /// </summary>
        [Test]
        public void Prewarm_ArrowsAreAlreadyFlyingAtTheStart()
        {
            SetParticleFields(new Vector2(0.5f, 1f), Vector2.zero, 1f);
            RowState row = LocalRow();
            row.StartRankUpStream(2.0);

            _stream.Render(row, 1.99);
            Assert.AreEqual(0, _stream.VisibleCount, "Trước mốc bắt đầu dòng chưa tồn tại.");

            _stream.Render(row, 2.0);
            Assert.That(_stream.VisibleCount, Is.InRange(3, 4));
        }

        [Test]
        public void RiseSpeedRange_ArrowsRiseFromTheStartHeightAtTheirOwnSpeed()
        {
            SetParticleFields(Vector2.zero, new Vector2(95f, 238f), 0f);
            var serialized = new UnityEditor.SerializedObject(_stream);
            serialized.FindProperty("riseRange").vector2Value = new Vector2(-0.25f, 5f);
            serialized.FindProperty("riseAcceleration").floatValue = 0f;
            serialized.FindProperty("openingBurst").intValue = 0;
            serialized.FindProperty("firstSpawnDelay").floatValue = 0f;
            serialized.FindProperty("fadeInFraction").floatValue = 0f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            RowState row = LocalRow();
            row.StartRankUpStream(0.0);
            const double age = 0.3;
            _stream.Render(row, age);
            RectTransform first = FirstVisibleArrow();
            Assert.IsNotNull(first);
            // Mũi đầu tiên (sinh ở 0) đã bay 0,3 s từ −0,25 × 180 = −45: quãng bay nằm giữa 95 và 238 đơn vị/giây × 0,3 s.
            float travelled = first.anchoredPosition.y + 45f;
            Assert.That(travelled, Is.InRange(95f * (float)age - 0.01f, 238f * (float)age + 0.01f));
        }

        [Test]
        public void ProjectedDisc_KeepsArrowsInsideTheRange_AndCrowdsTheMiddle()
        {
            SetParticleFields(Vector2.zero, Vector2.zero, 0f);
            var serialized = new UnityEditor.SerializedObject(_stream);
            serialized.FindProperty("horizontalDistribution").enumValueIndex =
                (int)LeaderboardRowRankUpStream.HorizontalDistribution.ProjectedDisc;
            serialized.FindProperty("horizontalRange").vector2Value = new Vector2(0f, 1f);
            serialized.FindProperty("poolSize").intValue = 64;
            serialized.FindProperty("spawnInterval").floatValue = 0.02f;
            serialized.FindProperty("lifetime").floatValue = 1f;
            serialized.FindProperty("openingBurst").intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            RowState row = LocalRow();
            row.StartRankUpStream(0.0);
            _stream.Render(row, 0.99);

            int middle = 0, total = 0;
            foreach (Transform child in _root.transform)
            {
                if (!child.gameObject.activeSelf) continue;
                float x = ((RectTransform)child).anchoredPosition.x;
                Assert.That(x, Is.InRange(-450.01f, 450.01f));
                total++;
                if (Mathf.Abs(x) < 225f) middle++;
            }
            Assert.Greater(total, 30);
            // Rải đều thì nửa giữa chiếm 50 %; hình chiếu đĩa (mật độ nửa hình tròn) chiếm ≈ 61 %.
            Assert.Greater(middle / (float)total, 0.55f);
        }

        private void SetParticleFields(Vector2 lifetimeRange, Vector2 riseSpeedRange, float prewarmDuration)
        {
            var serialized = new UnityEditor.SerializedObject(_stream);
            serialized.FindProperty("lifetimeRange").vector2Value = lifetimeRange;
            serialized.FindProperty("riseSpeedRange").vector2Value = riseSpeedRange;
            serialized.FindProperty("prewarmDuration").floatValue = prewarmDuration;
            serialized.FindProperty("spawnInterval").floatValue = 0.2f;
            serialized.FindProperty("firstSpawnDelay").floatValue = 0f;
            serialized.FindProperty("poolSize").intValue = 10;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private RectTransform FirstVisibleArrow()
        {
            foreach (Transform child in _root.transform)
            {
                if (child.gameObject.activeSelf && child.name != "Arrow") return (RectTransform)child;
            }
            return null;
        }

        private void AppendVisibleArrows(StringBuilder fingerprint)
        {
            foreach (Transform child in _root.transform)
            {
                if (!child.gameObject.activeSelf || child.name == "Arrow") continue;
                var arrow = (RectTransform)child;
                float alpha = child.GetComponent<Image>().color.a;
                fingerprint.Append(string.Format(CultureInfo.InvariantCulture, "{0:0.00},{1:0.00},{2:0.00},{3:0.000},{4:0.000};",
                                                 arrow.anchoredPosition.x, arrow.anchoredPosition.y, arrow.sizeDelta.x,
                                                 arrow.localScale.x, alpha));
            }
        }
    }
}
