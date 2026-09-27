using DreamTech.Leaderboard.ViewModel;
using NUnit.Framework;

namespace DreamTech.Leaderboard.Tests
{
    /// <summary>
    /// Cờ opt-in <see cref="MotionSettings.IntroUsesListBuffer"/> (0.6.0): đợt trượt vào gồm đúng các ô một list ảo hoá đang giữ
    /// view (<see cref="VirtualListLayout.BufferedSlots"/>) và <see cref="BoardModel.IntroSettleSeconds"/> là lúc ô CUỐI của cửa
    /// sổ đậu.
    ///
    /// <para><b>Số tham chiếu.</b> Hàng 218 + khe 6 = bước 224, lề trên list −23 (vùng bục 649 = 3 × 224 − 23), khung nhìn 1030,1,
    /// đệm tạo 200 / giữ 300, trượt 0,5 s, lệch 0,03 s, hàng đầu trễ 2 nhịp (0,06 s) — số của game tham chiếu. Nó giữ 3 hàng khi mở
    /// ở đỉnh (đợt trượt xong ở 0,62 s), 5 hàng khi canh giữa hạng 4 (0,68 s), 6 hàng ở hạng 5 (0,71 s), 8 hàng ở giữa list (0,77
    /// s; hàng đầu cửa sổ nằm TRÊN khung nhìn).</para>
    /// </summary>
    [TestFixture]
    public class ListIntroBufferTests
    {
        private const float ReferenceRowHeight = 218f;
        private const float ReferenceRowSpacing = 6f;
        private const float ReferenceTopPadding = -23f;
        private const float ReferenceBottomPadding = 72f;
        private const float ReferenceViewportHeight = 1030.1f;
        private const float CreateDistance = 200f;
        private const float RecycleDistance = 300f;
        private const int TopRanks = 3;
        private const float SettleTolerance = 1e-4f;

        private static readonly VirtualListLayout ReferenceLayout =
            new VirtualListLayout(ReferenceRowHeight, ReferenceRowSpacing, ReferenceTopPadding, ReferenceBottomPadding);

        private static MotionSettings Settings()
        {
            return new MotionSettings
            {
                HostPresentedTopRanks = TopRanks,
                IntroStagger = 0.03f,
                IntroDuration = 0.5f,
                IntroRowDelayOffset = 0.06f,
                MaximumIntroDelay = 1f,
                IntroUsesListBuffer = true,
                IntroBufferAbove = CreateDistance,
                IntroBufferBelow = CreateDistance,
                IntroBufferBelowRecentred = RecycleDistance,
            };
        }

        /// <summary>Như <c>LeaderboardScrollView</c>: cửa sổ đệm, đệm dưới lớn hơn khi cuộn khác 0, ô bục bị cắt ra.</summary>
        private static float SettleAt(int startRank, float scroll, out int firstSlot, out int slotCount)
        {
            MotionSettings settings = Settings();
            BoardScene scene = LeaderboardScenario.Create(maximumAnimatedPasses: 60).LoadRevealScene(startRank, 0);
            var model = new BoardModel(scene, settings);
            float bufferBelow = scroll > 0f ? settings.IntroBufferBelowRecentred : settings.IntroBufferBelow;
            ReferenceLayout.BufferedSlots(scroll, ReferenceViewportHeight, settings.IntroBufferAbove, bufferBelow,
                                          out firstSlot, out slotCount);
            int visibleFirst = System.Math.Max(firstSlot, model.HiddenLeadingSlots);
            model.StartIntro(visibleFirst, System.Math.Max(0, firstSlot + slotCount - visibleFirst));
            return model.IntroSettleSeconds;
        }

        private static float CenteredScroll(int slot)
        {
            return ReferenceLayout.CenteredScroll(slot, ReferenceViewportHeight, float.MaxValue);
        }

        [Test]
        public void Defaults_ListBufferIsOff()
        {
            var settings = new MotionSettings();
            Assert.IsFalse(settings.IntroUsesListBuffer);
            Assert.AreEqual(200f, settings.IntroBufferAbove);
            Assert.AreEqual(200f, settings.IntroBufferBelow);
            Assert.AreEqual(300f, settings.IntroBufferBelowRecentred);
            Assert.IsTrue(Settings().Clone().IntroUsesListBuffer, "Clone phải mang cờ theo.");
        }

        /// <summary>Mở ở đỉnh (người chơi trên bục): ba hàng dưới bục (ô 3–5), đợt trượt xong ở 0,62 s.</summary>
        [Test]
        public void OpenAtTheTop_HoldsThreeRowsBelowThePodium()
        {
            float settle = SettleAt(2, 0f, out int firstSlot, out int slotCount);
            Assert.AreEqual(5, firstSlot + slotCount - 1, "Ô cuối của cửa sổ.");
            Assert.AreEqual(0.62f, settle, SettleTolerance);
        }

        /// <summary>Canh giữa hạng 4 (cuộn 242,95): năm hàng, ô 3–7, xong ở 0,68 s.</summary>
        [Test]
        public void CentredOnRankFour_HoldsFiveRows()
        {
            float scroll = CenteredScroll(3);
            Assert.AreEqual(242.95f, scroll, 0.01f, "Tiền đề: cuộn canh giữa hạng 4 của bố cục tham chiếu.");
            float settle = SettleAt(3, scroll, out int firstSlot, out int slotCount);
            Assert.AreEqual(7, firstSlot + slotCount - 1);
            Assert.AreEqual(0.68f, settle, SettleTolerance);
        }

        /// <summary>Canh giữa hạng 5: sáu hàng (ô 3–8), xong ở 0,71 s.</summary>
        [Test]
        public void CentredOnRankFive_HoldsSixRows()
        {
            float settle = SettleAt(4, CenteredScroll(4), out int firstSlot, out int slotCount);
            Assert.AreEqual(8, firstSlot + slotCount - 1);
            Assert.AreEqual(0.71f, settle, SettleTolerance);
        }

        /// <summary>Canh giữa hạng 12: tám hàng (ô 8–15, hàng đầu nằm trên khung nhìn), xong ở 0,77 s.</summary>
        [Test]
        public void CentredMidList_HoldsEightRows_IncludingOneAboveTheViewport()
        {
            float scroll = CenteredScroll(11);
            float settle = SettleAt(11, scroll, out int firstSlot, out int slotCount);
            Assert.AreEqual(8, firstSlot);
            Assert.AreEqual(8, slotCount);
            Assert.Less(ReferenceLayout.SlotToTop(firstSlot) + ReferenceRowHeight, scroll, "Hàng đầu cửa sổ phải nằm trên khung nhìn.");
            Assert.AreEqual(0.77f, settle, SettleTolerance);
        }
    }
}
