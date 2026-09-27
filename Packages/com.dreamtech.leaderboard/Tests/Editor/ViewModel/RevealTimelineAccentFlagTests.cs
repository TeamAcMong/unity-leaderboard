using System.Collections.Generic;
using DreamTech.Leaderboard.ViewModel;
using NUnit.Framework;

namespace DreamTech.Leaderboard.Tests
{
    /// <summary>
    /// Hai cờ điểm nhấn: <see cref="MotionSettings.ScoreImprovedPill"/> (pill "BEST" khi có điểm mà không đổi hạng) và
    /// <see cref="MotionSettings.NewEntryAccent"/> (pill "NEW" + shine + loé khi mới vào bảng).
    ///
    /// <para><b>Mặc định phải y như cũ.</b> Dấu vân tay dưới đây được ghi từ code 0.5.0 TRƯỚC khi hai cờ tồn tại, với cả cấu
    /// hình mặc định lẫn cấu hình kiểu host (lật hạng sớm, host đếm điểm, cổng giữ Intro, nhịp nhẹ thay cú nhún). Đỏ ở đây
    /// nghĩa là mặc định của hai cờ đã làm đổi màn diễn của mọi game đang dùng package.</para>
    ///
    /// <para><b>Tắt cờ thì chỉ mất ĐIỂM NHẤN, không mất nhịp.</b> Nhịp <c>ScoreImproved</c> / <c>NewEntry</c> vẫn phát cho
    /// sink (âm thanh, host) và cú nở vẫn chạy — cờ chỉ bỏ phần vẽ thêm lên hàng.</para>
    /// </summary>
    [TestFixture]
    public class RevealTimelineAccentFlagTests
    {
        private const float FrameDeltaTime = 1f / 60f;
        private const int MaximumTicks = 4000;

        /// <summary>Thả cổng Intro ở tick này trong cấu hình kiểu host — cùng mốc với <see cref="RevealTimelineFlagOffTests"/>.</summary>
        private const int HostReleaseTick = 40;

        /// <summary>Hạng (0-based) và phần điểm cộng thêm của lượt "có điểm, không đổi hạng".</summary>
        private const int ScoreImprovedRank = 20;
        private const long ScoreImprovedGain = 5L;

        /// <summary>Hạng đích (0-based) của lượt "mới vào bảng".</summary>
        private const int NewEntryTargetRank = 20;

        private static IEnumerable<TestCaseData> Goldens()
        {
            // (mới vào bảng? — ngược lại là có điểm không đổi hạng, cấu hình kiểu host?, dấu vân tay ghi từ code trước khi có cờ)
            yield return new TestCaseData(false, false, 12999184346305153339UL).SetName("AccentFlagsDefault_ScoreImproved_MatchesRecordedTrajectory");
            yield return new TestCaseData(false, true, 2655062349338875682UL).SetName("AccentFlagsDefault_ScoreImprovedHostLike_MatchesRecordedTrajectory");
            yield return new TestCaseData(true, false, 10950174646409999965UL).SetName("AccentFlagsDefault_NewEntry_MatchesRecordedTrajectory");
            yield return new TestCaseData(true, true, 9992454637896049459UL).SetName("AccentFlagsDefault_NewEntryHostLike_MatchesRecordedTrajectory");
        }

        [TestCaseSource(nameof(Goldens))]
        public void AccentFlagsDefault_MatchRecordedTrajectory(bool isNewEntry, bool hostLike, ulong expected)
        {
            RevealTrace trace = Run(isNewEntry, Settings(hostLike));

            Assert.IsTrue(trace.Timeline.IsFinished, "Màn diễn phải kết thúc.");
            Assert.AreEqual(expected, trace.Fingerprint(),
                            "Màn diễn với cờ điểm nhấn MẶC ĐỊNH đã khác bản ghi từ code trước khi có cờ (" +
                            trace.Values.Count + " giá trị, " + trace.Beats.Count + " nhịp). Dấu vân tay mới: " +
                            trace.Fingerprint() + "UL");
        }

        [Test]
        public void ScoreImprovedPill_Default_ShowsBestPill()
        {
            RevealTrace trace = Run(isNewEntry: false, Settings(hostLike: true));

            Assert.AreEqual(RankChangeKind.ScoreImproved, trace.Model.Scene.Change.Kind);
            Assert.IsTrue(trace.Model.LocalRow.PillTiming.IsStarted, "Mặc định vẫn phải bật pill như cũ.");
            Assert.AreEqual(PillContent.Best, trace.Model.LocalRow.PillContent);
        }

        [Test]
        public void ScoreImprovedPill_Off_ShowsNoPill_ButTheBeatStillFires()
        {
            MotionSettings settings = Settings(hostLike: true);
            settings.ScoreImprovedPill = false;
            RevealTrace trace = Run(isNewEntry: false, settings);

            Assert.IsTrue(trace.Timeline.IsFinished);
            Assert.IsFalse(trace.Model.LocalRow.PillTiming.IsStarted, "Tắt cờ mà vẫn hiện pill BEST.");
            Assert.AreEqual(1, trace.CountOf(LeaderboardBeat.ScoreImproved), "Nhịp ScoreImproved vẫn phải tới sink.");
            Assert.AreEqual(trace.Model.Scene.Change.ToScore, trace.Model.LocalRow.DisplayScore,
                            "Tắt pill không được làm mất điểm cuối.");
        }

        [Test]
        public void NewEntryAccent_Default_ShowsNewPillShineAndFlash()
        {
            RevealTrace trace = Run(isNewEntry: true, new MotionSettings());

            Assert.AreEqual(RankChangeKind.NewEntry, trace.Model.Scene.Change.Kind);
            RowState local = trace.Model.LocalRow;
            Assert.AreEqual(PillContent.New, local.PillContent);
            Assert.IsTrue(local.PillTiming.IsStarted);
            Assert.IsTrue(local.ShineTiming.IsStarted);
            Assert.IsTrue(HasFlashed(trace), "Mặc định vẫn phải loé như cũ.");
        }

        [Test]
        public void NewEntryAccent_Off_PopsWithoutPillShineOrFlash()
        {
            MotionSettings settings = new MotionSettings { NewEntryAccent = false };
            RevealTrace trace = Run(isNewEntry: true, settings);

            Assert.IsTrue(trace.Timeline.IsFinished);
            RowState local = trace.Model.LocalRow;
            Assert.IsFalse(local.PillTiming.IsStarted, "Tắt cờ mà vẫn hiện pill NEW.");
            Assert.IsFalse(local.ShineTiming.IsStarted, "Tắt cờ mà vẫn quét shine.");
            Assert.IsFalse(HasFlashed(trace), "Tắt cờ mà hàng vẫn loé.");
            Assert.AreEqual(1, trace.CountOf(LeaderboardBeat.NewEntry), "Nhịp NewEntry vẫn phải tới sink.");
            Assert.AreEqual(1, trace.LandedCount, "OnLanded (sao của host) không thuộc cờ này.");
            Assert.AreEqual(1f, local.Scale, 0.0001f, "Cú nở vẫn phải về cỡ thật.");
        }

        private static MotionSettings Settings(bool hostLike)
        {
            if (!hostLike) return new MotionSettings();
            MotionSettings settings = RevealTimelineFlagOffTests.HostLikeSettings();
            settings.QuietPulseInsteadOfBob = true;
            return settings;
        }

        private static RevealTrace Run(bool isNewEntry, MotionSettings settings)
        {
            LeaderboardScenario scenario = LeaderboardScenario.Create(maximumAnimatedPasses: 15);
            BoardScene scene = isNewEntry
                ? scenario.LoadRevealScene(-1, NewEntryTargetRank)
                : scenario.LoadScoreImprovedScene(ScoreImprovedRank, ScoreImprovedGain);
            return RevealTrace.Run(scene, settings, FrameDeltaTime, MaximumTicks, (trace, tick) =>
            {
                if (tick == HostReleaseTick) trace.Timeline.ReleaseHostHold();
            });
        }

        /// <summary>
        /// Có frame nào hàng mình loé không. Độ loé là giá trị THỨ 6 của phần "row mình" trong mỗi frame của
        /// <see cref="RevealTrace"/> — ở đây đọc thẳng từ lần chạy lại cho khỏi phụ thuộc bố cục bản ghi.
        /// </summary>
        private static bool HasFlashed(RevealTrace trace)
        {
            return trace.MaximumLocalFlash > 0f;
        }
    }
}
