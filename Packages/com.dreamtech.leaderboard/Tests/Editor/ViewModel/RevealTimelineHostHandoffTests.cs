using DreamTech.Leaderboard.ViewModel;
using NUnit.Framework;

namespace DreamTech.Leaderboard.Tests
{
    /// <summary>
    /// Ba cờ bàn giao quyền cho host: <see cref="MotionSettings.WaitForHostRelease"/>,
    /// <see cref="MotionSettings.HostOwnsScoreCount"/> và <see cref="MotionSettings.RankFlipsBeforeRankMove"/>.
    ///
    /// <para><b>Vì sao cả ba cần test.</b> Mỗi cờ đều MẶC ĐỊNH TẮT để không đổi hành vi của game đang chạy —
    /// nghĩa là đường code mới chỉ sống khi có người bật nó lên. Loại code đó không ai vô tình chạy qua, nên
    /// nếu nó hỏng thì hỏng im lặng cho tới đúng cái game bật nó.</para>
    ///
    /// <para><b>Và một cái bẫy cụ thể:</b> widget chụp <c>MotionSettings</c> bằng <c>MemberwiseClone</c>. Hôm
    /// nay điều đó copy cả field mới; ngày ai đó đổi <c>Clone()</c> thành phép gán tay từng field, ba cờ này
    /// sẽ rơi IM LẶNG và mọi thứ vẫn biên dịch. Test cuối canh đúng chuyện đó.</para>
    /// </summary>
    [TestFixture]
    public class RevealTimelineHostHandoffTests
    {
        private const float FrameDeltaTime = 1f / 60f;
        private const int MaximumTicks = 4000;

        /// <summary>
        /// Số tick để vừa QUA pha Intro (IntroWait mặc định 0,3 s = 18 tick) và nằm gọn đầu pha Lift.
        ///
        /// <para>Đây là khoảnh khắc duy nhất phân biệt được hai cờ: <c>BeginAfterIntro</c> đã chạy (nên cờ lật
        /// hạng sớm đã kịp đổi số), nhưng Lift chỉ đụng Scale/Lift/Glow — chưa có gì dời Slot hay đổi hạng theo
        /// đường thường. Chọn sớm hơn (bản đầu dùng 3 tick = 0,05 s) là test chưa tới được đoạn code nó định
        /// kiểm, và đỏ vì một lý do chẳng liên quan.</para>
        /// </summary>
        private const int TicksIntoLift = 20;

        /// <summary>
        /// Vào sâu pha Climb nhưng CHƯA tới Land. Với kịch bản 120→108 (12 hàng × 0,09 s = 1,08 s leo), Land
        /// rơi vào khoảng 0,3 + 0,22 + 1,08 = 1,6 s = 96 tick; 60 tick là giữa cú leo — đúng chỗ mà bản cũ
        /// đang nội suy bộ đếm, nên đúng chỗ phải chứng minh là nó KHÔNG còn nội suy.
        /// </summary>
        private const int TicksIntoClimb = 60;

        private static BoardModel NewRankUpModel(MotionSettings settings)
        {
            LeaderboardScenario scenario = LeaderboardScenario.Create(maximumAnimatedPasses: 18);
            BoardScene scene = scenario.LoadRevealScene(120, 108);
            return new BoardModel(scene, settings);
        }

        private static void Tick(RevealTimeline timeline, BoardModel model, int ticks)
        {
            for (int index = 0; index < ticks && !timeline.IsFinished; index++)
            {
                timeline.Tick(FrameDeltaTime);
                model.Advance(FrameDeltaTime);
            }
        }

        /// <summary>
        /// Chưa thả cổng thì màn diễn PHẢI đứng ở Intro — không được nhích sang Lift.
        ///
        /// <para>Đây là toàn bộ lý do cờ này tồn tại: host có một nhịp riêng (dòng phần thưởng rót xuống row)
        /// phải diễn xong trước khi bảng xếp lại. Rò một nhịp là hai chương chồng lên nhau.</para>
        /// </summary>
        [Test]
        public void WaitForHostRelease_HoldsAtIntro_UntilReleased()
        {
            var settings = new MotionSettings { WaitForHostRelease = true, HostHoldTimeout = 100f };
            BoardModel model = NewRankUpModel(settings);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());

            timeline.Start();
            Tick(timeline, model, 120);               // 2 giây — thừa sức qua IntroWait 0,3 s

            Assert.AreEqual(RevealPhase.Intro, timeline.Phase,
                            "Chưa thả cổng mà màn diễn đã rời Intro — cổng giữ không có tác dụng.");
            Assert.IsFalse(timeline.IsFinished);

            timeline.ReleaseHostHold();
            Tick(timeline, model, 5);

            Assert.AreNotEqual(RevealPhase.Intro, timeline.Phase,
                               "Đã thả cổng mà vẫn kẹt ở Intro.");
        }

        /// <summary>Thả cổng TRƯỚC khi diễn cũng phải hợp lệ — host không nên phải canh thời điểm.</summary>
        [Test]
        public void ReleaseHostHold_BeforeStart_DoesNotHold()
        {
            var settings = new MotionSettings { WaitForHostRelease = true, HostHoldTimeout = 100f };
            BoardModel model = NewRankUpModel(settings);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());

            timeline.ReleaseHostHold();
            timeline.Start();
            Tick(timeline, model, 60);

            Assert.AreNotEqual(RevealPhase.Intro, timeline.Phase,
                               "Thả cổng trước khi Start mà vẫn bị giữ.");
        }

        /// <summary>
        /// Host quên thả cổng thì màn diễn vẫn phải chạy tiếp sau <see cref="MotionSettings.HostHoldTimeout"/>,
        /// và phải KÊU LÊN.
        ///
        /// <para>Một màn xếp hạng đứng hình vĩnh viễn là thứ tệ nhất cái cờ này có thể gây ra, nên nó cần một
        /// đường thoát không phụ thuộc vào ai.</para>
        /// </summary>
        [Test]
        public void WaitForHostRelease_TimesOut_AndFlagsIt()
        {
            var settings = new MotionSettings { WaitForHostRelease = true, HostHoldTimeout = 0.5f };
            BoardModel model = NewRankUpModel(settings);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());

            timeline.Start();
            Tick(timeline, model, MaximumTicks);

            Assert.IsTrue(timeline.IsFinished, "Hết giờ chờ mà màn diễn vẫn treo.");
            Assert.IsTrue(timeline.HostHoldTimedOut,
                          "Hết giờ chờ host mà không đặt cờ — lớp UI sẽ không có gì để kêu.");
        }

        /// <summary>Không bật cờ thì không có gì đổi: đường mặc định phải y như trước.</summary>
        [Test]
        public void WithoutTheFlag_NothingHolds()
        {
            BoardModel model = NewRankUpModel(new MotionSettings());
            var timeline = new RevealTimeline(model, new RecordingRevealListener());

            timeline.Start();
            Tick(timeline, model, MaximumTicks);

            Assert.IsTrue(timeline.IsFinished);
            Assert.IsFalse(timeline.HostHoldTimedOut, "Không bật cổng mà lại báo hết giờ chờ.");
        }

        /// <summary>
        /// Host tự đếm ⇒ timeline không được chạm vào con số trong lúc row đang leo.
        ///
        /// <para>Đặc biệt là không được ghi "về FromScore" ở đầu: host có thể đã nhích bộ đếm lên rồi, và một
        /// cú giật lùi ngay trước mắt người chơi là thứ đọc ra như mất điểm.</para>
        /// </summary>
        [Test]
        public void HostOwnsScoreCount_TimelineLeavesTheNumberAlone()
        {
            var settings = new MotionSettings { HostOwnsScoreCount = true };
            BoardModel model = NewRankUpModel(settings);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());

            // Start() → Prepare() tua row về trạng thái CŨ (hạng cũ, điểm cũ, chỗ cũ): model được dựng sẵn ở
            // trạng thái CUỐI, và Prepare là thứ quay ngược nó lại (RankUpPlanner.Prepare). Nên host chỉ có thể
            // bắt đầu đếm SAU Start — đúng như ngoài đời, nơi host nghe RevealStarted rồi mới rót token.
            timeline.Start();

            long midway = (model.Scene.Change.FromScore + model.Scene.Change.ToScore) / 2;
            model.LocalRow.SetDisplayScore(midway, model.Clock, false);

            Tick(timeline, model, TicksIntoClimb);

            Assert.AreEqual(midway, model.LocalRow.DisplayScore,
                            "Timeline đã ghi đè con số của host giữa màn diễn — hai bên đang cãi nhau một con số.");
        }

        /// <summary>Dù host sở hữu bộ đếm, điểm cuối VẪN phải đúng — không được nuốt điểm vì lý do trình bày.</summary>
        [Test]
        public void HostOwnsScoreCount_StillSettlesToFinalScore()
        {
            var settings = new MotionSettings { HostOwnsScoreCount = true };
            BoardModel model = NewRankUpModel(settings);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());

            timeline.Start();
            Tick(timeline, model, MaximumTicks);

            Assert.IsTrue(timeline.IsFinished);
            Assert.AreEqual(model.Scene.Change.ToScore, model.LocalRow.DisplayScore,
                            "Host sở hữu bộ đếm không có nghĩa là timeline được phép bỏ mặc con số cuối.");
        }

        /// <summary>
        /// Bật cờ lật hạng sớm ⇒ số hạng đổi NGAY, trước khi row nhúc nhích.
        ///
        /// <para>Và <c>Slot</c> thì KHÔNG được đổi theo: row vẫn phải bò lên, vì chính cú bò đó làm danh sách
        /// cuộn. Đổi cả Slot là nhảy cóc tới đích và mất sạch chuyển động.</para>
        /// </summary>
        [Test]
        public void RankFlipsBeforeRankMove_ChangesTheNumberImmediately()
        {
            var settings = new MotionSettings { RankFlipsBeforeRankMove = true };
            BoardModel model = NewRankUpModel(settings);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());

            timeline.Start();

            // Chụp SAU Start: Prepare vừa tua Slot về chỗ xuất phát của cú leo.
            float slotBefore = model.LocalRow.Slot;

            Tick(timeline, model, TicksIntoLift);

            Assert.AreEqual(model.Scene.Change.ToRank, model.LocalRow.DisplayRank,
                            "Bật RankFlipsBeforeRankMove mà số hạng vẫn là hạng cũ.");
            Assert.AreEqual(slotBefore, model.LocalRow.Slot, 0.001f,
                            "Lật hạng sớm KHÔNG được dời chỗ row — cú bò lên mới là thứ làm danh sách cuộn.");
        }

        /// <summary>Tắt cờ ⇒ giữ ẩn dụ 'leo': hạng cũ còn nguyên trong lúc row đang đi.</summary>
        [Test]
        public void WithoutRankFlipFlag_RankStaysOldWhileClimbing()
        {
            BoardModel model = NewRankUpModel(new MotionSettings());
            var timeline = new RevealTimeline(model, new RecordingRevealListener());

            timeline.Start();

            // Chụp SAU Start: trước đó row còn đang ở hạng CUỐI (model dựng sẵn trạng thái sau), và so với con
            // số đó là so với đúng cái đích mà màn diễn sắp đi tới.
            int rankBefore = model.LocalRow.DisplayRank;
            Assert.AreEqual(model.Scene.Change.FromRank, rankBefore,
                            "Tiền đề của test hỏng: Prepare không tua row về hạng cũ.");

            Tick(timeline, model, TicksIntoLift);

            Assert.AreEqual(rankBefore, model.LocalRow.DisplayRank,
                            "Không bật cờ mà số hạng đã đổi ngay đầu — đổi hành vi mặc định là đổi mọi game đang chạy.");
        }

        /// <summary>
        /// Đã lật sớm thì con số PHẢI đứng yên ở hạng cuối suốt phần còn lại của màn diễn.
        ///
        /// <para><b>Lỗi thật đã xảy ra:</b> bản đầu của cờ này lật 48 → 37 ở đầu pha Lift, nhưng
        /// <c>CrossNext</c> vẫn ghi "hạng của người vừa vượt − 1" ở MỖI lần vượt — nên 0,23 s sau con số lăn
        /// ngược về 47, 46, … rồi mới về lại 37, và lăn theo chiều "tụt hạng". Tệ hơn cả khi chưa có cờ. Test
        /// này soi TỪNG tick chứ không chỉ khung cuối: khung cuối lúc nào cũng đúng, cái sai nằm ở giữa.</para>
        /// </summary>
        [TestCase(120, 108, TestName = "RankFlipsEarly_NumberNeverRollsBack_Climb")]
        [TestCase(900, 600, TestName = "RankFlipsEarly_NumberNeverRollsBack_CompressedSpin")]
        public void RankFlipsBeforeRankMove_NumberNeverLeavesTheFinalRank(int startRank, int targetRank)
        {
            var settings = new MotionSettings { RankFlipsBeforeRankMove = true };
            LeaderboardScenario scenario = LeaderboardScenario.Create(maximumAnimatedPasses: 18);
            BoardScene scene = scenario.LoadRevealScene(startRank, targetRank);
            var model = new BoardModel(scene, settings);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());

            timeline.Start();
            Tick(timeline, model, TicksIntoLift);
            int finalRank = scene.Change.ToRank;
            Assert.AreEqual(finalRank, model.LocalRow.DisplayRank, "Tiền đề hỏng: chưa lật hạng.");

            for (int tick = 0; tick < MaximumTicks && !timeline.IsFinished; tick++)
            {
                timeline.Tick(FrameDeltaTime);
                model.Advance(FrameDeltaTime);
                Assert.AreEqual(finalRank, model.LocalRow.DisplayRank,
                                "Tick " + tick + " (pha " + timeline.Phase + "): số hạng rời khỏi hạng cuối sau " +
                                "khi đã lật — con số đang lăn ngược.");
            }
            Assert.IsTrue(timeline.IsFinished);
        }

        /// <summary>
        /// Nhịp "đáp" (sao vàng, pill ▲N) diễn ĐÚNG MỘT lần, và diễn NGAY lúc lật chứ không đợi tới lúc đáp.
        ///
        /// <para>Bản đầu gọi pill ở cả hai chỗ: <c>RowState.StartPill</c> đặt lại đồng hồ pill nên nó nảy lại
        /// giữa lúc đang nổi lên. Còn sao vàng thì nằm lại ở lúc đáp — bung ra SAU mũi tên cả giây, ngược thứ
        /// tự của clip (khung 10,44: sao và mũi tên cùng lúc với cú lật).</para>
        /// </summary>
        [Test]
        public void RankFlipsBeforeRankMove_LandingFeedbackPlaysOnce_AtTheFlip()
        {
            var settings = new MotionSettings { RankFlipsBeforeRankMove = true };
            BoardModel model = NewRankUpModel(settings);
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);

            timeline.Start();
            Tick(timeline, model, TicksIntoLift);
            Assert.AreEqual(1, listener.LandedCount,
                            "Lật hạng rồi mà sao vàng chưa bung — nó vẫn đang đợi tới lúc đáp.");

            Tick(timeline, model, MaximumTicks);
            Assert.IsTrue(timeline.IsFinished);
            Assert.AreEqual(1, listener.LandedCount,
                            "Nhịp đáp diễn hai lần (lúc lật và lúc đáp) — pill sẽ nảy lại giữa chừng.");
        }

        /// <summary>
        /// Bỏ qua trong lúc host còn đang giữ cổng — TRƯỚC cả khi kịp lật — vẫn phải có sao và pill.
        ///
        /// <para>Đây là đường mà "tắt nhịp đáp ở BeginLand khi cờ bật" sẽ nuốt mất: cờ bật, nhưng nhịp sớm
        /// chưa bao giờ chạy. Người chơi bấm bỏ qua thì mất luôn phần thưởng thị giác của việc lên hạng.</para>
        /// </summary>
        [Test]
        public void SkipDuringHostHold_StillPlaysLandingFeedbackOnce()
        {
            var settings = new MotionSettings
            {
                RankFlipsBeforeRankMove = true,
                WaitForHostRelease = true,
                HostHoldTimeout = 100f,
            };
            BoardModel model = NewRankUpModel(settings);
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);

            timeline.Start();
            Tick(timeline, model, 60);                         // đang bị giữ ở Intro
            Assert.AreEqual(RevealPhase.Intro, timeline.Phase, "Tiền đề hỏng: cổng không giữ.");
            Assert.AreEqual(0, listener.LandedCount);

            timeline.RequestSkip();
            Tick(timeline, model, MaximumTicks);

            Assert.IsTrue(timeline.IsFinished);
            Assert.AreEqual(1, listener.LandedCount,
                            "Bỏ qua lúc đang giữ cổng làm mất sao/pill lên hạng.");
            Assert.AreEqual(model.Scene.Change.ToRank, model.LocalRow.DisplayRank);
        }

        /// <summary>Không bật cờ ⇒ nhịp đáp vẫn diễn đúng một lần, ở lúc đáp như trước.</summary>
        [Test]
        public void WithoutRankFlipFlag_LandingFeedbackStillAtLand()
        {
            BoardModel model = NewRankUpModel(new MotionSettings());
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);

            timeline.Start();
            Tick(timeline, model, TicksIntoLift);
            Assert.AreEqual(0, listener.LandedCount, "Không bật cờ mà sao vàng đã bung ngay đầu.");

            Tick(timeline, model, MaximumTicks);
            Assert.AreEqual(1, listener.LandedCount);
        }

        /// <summary>
        /// <c>Clone()</c> phải mang theo MỌI field, kể cả ba cờ mới.
        ///
        /// <para>Widget chụp settings bằng <c>Clone()</c> trước khi diễn. Nếu phép chụp đó bỏ sót field, cờ
        /// bật trong asset sẽ KHÔNG tới được timeline — không lỗi, không log, chỉ là tính năng lặng lẽ không
        /// chạy. Đúng loại hỏng mà bộ test này có mặt để chặn.</para>
        /// </summary>
        [Test]
        public void Clone_CarriesTheHandoffFlags()
        {
            var settings = new MotionSettings
            {
                WaitForHostRelease = true,
                HostOwnsScoreCount = true,
                RankFlipsBeforeRankMove = true,
                HostHoldTimeout = 3.5f,
            };

            MotionSettings copy = settings.Clone();

            Assert.IsTrue(copy.WaitForHostRelease, "Clone() đánh rơi WaitForHostRelease.");
            Assert.IsTrue(copy.HostOwnsScoreCount, "Clone() đánh rơi HostOwnsScoreCount.");
            Assert.IsTrue(copy.RankFlipsBeforeRankMove, "Clone() đánh rơi RankFlipsBeforeRankMove.");
            Assert.AreEqual(3.5f, copy.HostHoldTimeout, "Clone() đánh rơi HostHoldTimeout.");
        }
    }
}
