using System;
using System.Collections.Generic;
using DreamTech.Leaderboard.ViewModel;
using NUnit.Framework;

namespace DreamTech.Leaderboard.Tests
{
    /// <summary>
    /// <see cref="MotionSettings.HostPresentedTopRanks"/>: các hạng đầu do host trình bày (bục), màn lên hạng đáp vào đó leo
    /// trong list tới ranh giới, dừng ở <see cref="RevealPhase.PodiumHold"/> chờ host, rồi đi nốt.
    ///
    /// <para><b>Ba điều phải đúng tuyệt đối.</b> (1) Màn lên hạng KHÔNG đáp vào bục diễn y hệt khi cờ tắt — chúng đã được chỉnh
    /// từng khung; test so từng tick, từng bit. (2) Không có đường nào treo màn diễn: thả sớm, không bao giờ thả, bỏ qua ở mọi tick,
    /// đóng giữa lúc chờ. (3) Hình của host và hình của list khớp nhau: ai đứng ở ô nào lúc host nhận quyền (người cũ hạng 3 vẫn ở
    /// ô 2, row mình ở ô ranh giới).</para>
    /// </summary>
    [TestFixture]
    public class RevealTimelinePodiumTests
    {
        private const float FrameDeltaTime = 1f / 60f;
        private const int MaximumTicks = 4000;
        private const int TopRanks = 3;

        /// <summary>Host "diễn" cú lên bục trong ngần này tick rồi mới thả cổng.</summary>
        private const int ReleaseAfterHoldTicks = 20;

        /// <summary>Đủ dài để không bao giờ hết giờ trong một test không cố ý chờ hết giờ.</summary>
        private const float PatientTimeout = 100f;

        private static MotionSettings PodiumSettings(float podiumClimbDuration = 0f, float podiumPassSlideDuration = 0f,
                                                     float holdTimeout = PatientTimeout)
        {
            return new MotionSettings
            {
                HostPresentedTopRanks = TopRanks,
                PodiumClimbDuration = podiumClimbDuration,
                PodiumPassSlideDuration = podiumPassSlideDuration,
                HostHoldTimeout = holdTimeout,
            };
        }

        private static BoardScene LoadScene(int startRank, int targetRank, int maximumAnimatedPasses = 15)
        {
            return LeaderboardScenario.Create(maximumAnimatedPasses: maximumAnimatedPasses).LoadRevealScene(startRank, targetRank);
        }

        /// <summary>
        /// Chạy trọn màn diễn; host thả cổng bục <paramref name="releaseAfterHoldTicks"/> tick sau lần đầu thấy PodiumHold (-1 =
        /// không bao giờ thả). <paramref name="probe"/> được gọi trước mỗi tick — lúc đó trạng thái là trạng thái cuối tick trước.
        /// </summary>
        private static RevealTrace RunWithRelease(BoardScene scene, MotionSettings settings, int releaseAfterHoldTicks,
                                                  int skipAtTick = -1, Action<RevealTrace, int> probe = null)
        {
            int holdSeenAtTick = -1;
            return RevealTrace.Run(scene, settings, FrameDeltaTime, MaximumTicks, (trace, tick) =>
            {
                if (holdSeenAtTick < 0 && trace.Timeline.Phase == RevealPhase.PodiumHold) holdSeenAtTick = tick;
                probe?.Invoke(trace, tick);
                if (releaseAfterHoldTicks >= 0 && holdSeenAtTick >= 0 && tick == holdSeenAtTick + releaseAfterHoldTicks)
                {
                    trace.Timeline.ReleasePodiumHold();
                }
                if (tick == skipAtTick) trace.Timeline.RequestSkip();
            });
        }

        private static RowState RowOfRank(BoardModel model, int rank)
        {
            IReadOnlyList<RowState> rows = model.Rows;
            for (int index = 0; index < rows.Count; index++)
            {
                if (!rows[index].IsGap && rows[index].Entry.Rank == rank) return rows[index];
            }
            return null;
        }

        private static void AssertLandedAndSettled(RevealTrace trace, string label)
        {
            RankChange change = trace.Model.Scene.Change;
            Assert.IsTrue(trace.Timeline.IsFinished, label + ": màn diễn phải kết thúc.");
            Assert.IsTrue(trace.Timeline.HasReachedLanding, label + ": phải chạm nhịp hạ cánh.");
            Assert.AreEqual(change.ToRank, trace.Model.LocalRow.DisplayRank, label + ": hạng cuối.");
            Assert.AreEqual(change.ToScore, trace.Model.LocalRow.DisplayScore, label + ": điểm cuối.");
            Assert.AreEqual(trace.Timeline.Plan.FinalSlot, trace.Model.LocalRow.Slot, 1e-4f, label + ": row mình đúng ô cuối.");
            Assert.AreEqual(trace.Timeline.Plan.LocalIndex, trace.Model.IndexOf(trace.Model.LocalRow), label + ": ô cuối == index.");
            Assert.AreEqual(1, trace.CountOf(LeaderboardBeat.Land), label + ": đúng 1 nhịp Land.");
            Assert.AreEqual(1, trace.CountOf(LeaderboardBeat.RevealFinished), label + ": đúng 1 nhịp RevealFinished.");
            Assert.AreEqual(trace.Beats.Count - 1, trace.LastIndexOfBeat(LeaderboardBeat.RevealFinished),
                            label + ": RevealFinished phải là nhịp cuối.");
            Assert.LessOrEqual(trace.CountOf(LeaderboardBeat.PodiumTakeover), 1, label + ": PodiumTakeover phát tối đa một lần.");
            trace.Model.FinishAllTweens();
            RankUpPlannerInvariantTests.AssertRowsSettled(trace.Model, label);
        }

        // ---------------------------------------------------------------- Cờ bật nhưng không đáp vào bục: y hệt cờ tắt

        /// <summary>
        /// Game bật cờ (Golden Race đặt 3) thì mọi màn lên hạng KHÔNG đáp vào bục vẫn phải diễn y hệt khi cờ tắt — từng tick, từng
        /// bit: slot và hạng của mọi row, cỡ / nhấc / glow / điểm / flash của row mình, mọi nhịp và mọi lời gọi listener.
        ///
        /// <para>Gồm cả ca sát nút (đáp ĐÚNG ô ranh giới, #21 → #4) và ca quay số có đuôi: ranh giới nằm ngay trên đích mà màn diễn
        /// vẫn không được lệch một nhịp nào.</para>
        /// </summary>
        [TestCase(47, 36, false, -1, TestName = "FlagOn_NonPodium_48To37_IdenticalToFlagOff")]
        [TestCase(119, 107, false, -1, TestName = "FlagOn_NonPodium_120To108_IdenticalToFlagOff")]
        [TestCase(899, 599, false, -1, TestName = "FlagOn_NonPodium_CompressedSpin_IdenticalToFlagOff")]
        [TestCase(299, 4, false, -1, TestName = "FlagOn_NonPodium_CompressedIntoTopListWithTail_IdenticalToFlagOff")]
        [TestCase(20, 3, false, -1, TestName = "FlagOn_NonPodium_LandsExactlyOnBoundary_IdenticalToFlagOff")]
        [TestCase(47, 36, true, -1, TestName = "FlagOn_NonPodium_HostLike_48To37_IdenticalToFlagOff")]
        [TestCase(20, 3, true, -1, TestName = "FlagOn_NonPodium_HostLike_LandsExactlyOnBoundary_IdenticalToFlagOff")]
        [TestCase(47, 36, true, 50, TestName = "FlagOn_NonPodium_HostLike_48To37_Skipped_IdenticalToFlagOff")]
        public void FlagOn_NonPodiumRankUp_IsIdenticalToFlagOff(int startRank, int targetRank, bool hostLike, int skipAtTick)
        {
            MotionSettings flagOff = hostLike ? RevealTimelineFlagOffTests.HostLikeSettings() : new MotionSettings();
            MotionSettings flagOn = flagOff.Clone();
            flagOn.HostPresentedTopRanks = TopRanks;
            flagOn.PodiumClimbDuration = 0.4f;
            flagOn.PodiumPassSlideDuration = 0.2f;

            RevealTrace reference = RevealTimelineFlagOffTests.RunScenario(startRank, targetRank, flagOff, skipAtTick);
            RevealTrace candidate = RevealTimelineFlagOffTests.RunScenario(startRank, targetRank, flagOn, skipAtTick);

            Assert.IsFalse(candidate.Timeline.TakesPodium, "Tiền đề hỏng: màn diễn này không được lên bục.");
            Assert.Greater(candidate.Model.HiddenLeadingSlots, 0, "Tiền đề hỏng: cờ bật mà không có ô nào do host trình bày.");
            CollectionAssert.AreEqual(reference.Beats, candidate.Beats, "Chuỗi nhịp khác khi bật cờ.");
            CollectionAssert.AreEqual(reference.Values, candidate.Values,
                                      "Quỹ đạo từng tick khác khi bật cờ — một màn lên hạng thường đã bị cờ bục chạm vào.");
        }

        /// <summary>Cờ tắt (0, mặc định) thì timeline không bao giờ coi một màn diễn là lên bục, kể cả khi đáp vào top 3.</summary>
        [TestCase(9, 1)]
        [TestCase(2, 1)]
        [TestCase(5, 0)]
        public void FlagOff_NeverTakesPodium(int startRank, int targetRank)
        {
            RevealTrace trace = RevealTimelineFlagOffTests.RunScenario(startRank, targetRank, new MotionSettings(), -1);

            Assert.AreEqual(0, trace.Model.HiddenLeadingSlots);
            Assert.IsFalse(trace.Timeline.TakesPodium);
            Assert.AreEqual(0, trace.CountOf(LeaderboardBeat.PodiumTakeover));
            CollectionAssert.DoesNotContain(trace.Phases, RevealPhase.PodiumHold);
            CollectionAssert.DoesNotContain(trace.Phases, RevealPhase.PodiumClimb);
        }

        // ---------------------------------------------------------------- Lên bục từ list

        /// <summary>
        /// #10 → #2: leo trong list tới ô ranh giới (ô 3), DỪNG, phát PodiumTakeover đúng một lần sau mọi nhịp Pass của đoạn trong
        /// list; lúc đứng chờ thì người cũ hạng 3 vẫn ở ô 2 và người cũ hạng 2 vẫn ở ô 1 (host dựng bục "trước" từ đó); thả cổng →
        /// PodiumClimb → Land → Celebrate → RevealFinished, ô cuối == index.
        /// </summary>
        [TestCase(0.35f, TestName = "PromotionFromList_AnimatedPodiumClimb_HoldsAtBoundary_ThenClimbsLandsAndCelebrates")]
        [TestCase(0f, TestName = "PromotionFromList_SnappedPodiumClimb_HoldsAtBoundary_ThenLandsAndCelebrates")]
        public void PromotionFromList_HoldsAtBoundary_ThenClimbsLandsAndCelebrates(float podiumClimbDuration)
        {
            BoardScene scene = LoadScene(9, 1);
            int holdTicks = 0;
            bool isStateCheckedDuringHold = false;

            RevealTrace trace = RunWithRelease(scene, PodiumSettings(podiumClimbDuration: podiumClimbDuration), ReleaseAfterHoldTicks, -1,
                (run, tick) =>
                {
                    if (run.Timeline.Phase != RevealPhase.PodiumHold) return;
                    holdTicks++;
                    BoardModel model = run.Model;
                    Assert.AreEqual(TopRanks, model.LocalRow.Slot, 1e-4f, "Row mình phải đứng ở ô ranh giới lúc chờ.");
                    Assert.AreEqual(1f, model.ListPresence(model.LocalRow), 1e-4f, "Ở ô ranh giới row mình vẫn nằm trong list.");
                    Assert.AreEqual(2f, RowOfRank(model, 3).Slot, 1e-4f, "Người cũ hạng 3 phải còn ở ô 2 (trên bục) lúc host nhận quyền.");
                    Assert.AreEqual(1f, RowOfRank(model, 2).Slot, 1e-4f, "Người cũ hạng 2 phải còn ở ô 1.");
                    Assert.AreEqual(0f, model.ListPresence(RowOfRank(model, 3)), "Người cũ hạng 3 đang sau bục: list không vẽ.");
                    Assert.AreEqual(4f, RowOfRank(model, 4).TargetSlot, 1e-4f, "Người bị vượt cuối cùng trong list phải đã nhường ô.");
                    Assert.IsFalse(run.Timeline.HasReachedLanding, "Đang chờ host thì chưa được coi là đã đáp.");
                    isStateCheckedDuringHold = true;
                });

            Assert.IsTrue(trace.Timeline.TakesPodium);
            Assert.IsTrue(isStateCheckedDuringHold, "Màn diễn chưa bao giờ đứng ở cổng bục.");
            Assert.GreaterOrEqual(holdTicks, ReleaseAfterHoldTicks, "Cổng bục không giữ được tới lúc host thả.");
            Assert.IsFalse(trace.Timeline.PodiumHoldTimedOut);

            if (podiumClimbDuration > 0f)
            {
                AssertPhaseOrder(trace, RevealPhase.Lift, RevealPhase.Climb, RevealPhase.PodiumHold, RevealPhase.PodiumClimb,
                                 RevealPhase.Land, RevealPhase.Finished);
            }
            else
            {
                // PodiumClimb dài 0 giây xong ngay trong tick thả cổng nên không bao giờ là pha cuối một tick.
                AssertPhaseOrder(trace, RevealPhase.Lift, RevealPhase.Climb, RevealPhase.PodiumHold, RevealPhase.Land,
                                 RevealPhase.Finished);
            }

            int takeover = trace.IndexOfBeat(LeaderboardBeat.PodiumTakeover);
            Assert.AreEqual(1, trace.CountOf(LeaderboardBeat.PodiumTakeover));
            Assert.Greater(takeover, trace.IndexOfBeat(LeaderboardBeat.Lift));
            Assert.Greater(trace.CountOf(LeaderboardBeat.Pass), 0, "Đoạn trong list phải có nhịp vượt.");
            Assert.Less(trace.IndexOfBeat(LeaderboardBeat.Pass), takeover, "Nhịp Pass của đoạn trong list phải tới trước PodiumTakeover.");
            if (podiumClimbDuration <= 0f)
            {
                // Chỉ nhịp Pass ĐẦU TIÊN trước cổng thì chưa đủ: một nhịp Pass lọt ra SAU cổng (đoạn trong list vượt quá ranh giới,
                // hay đoạn 0 giây dồn nhịp vào frame thả) sẽ không bị bắt.
                Assert.Less(trace.LastIndexOfBeat(LeaderboardBeat.Pass), takeover,
                            "Đoạn sau cổng 0 giây thì MỌI nhịp Pass phải tới trước PodiumTakeover.");
            }
            Assert.Greater(trace.IndexOfBeat(LeaderboardBeat.Land), takeover, "Land phải sau PodiumTakeover.");
            Assert.Greater(trace.IndexOfBeat(LeaderboardBeat.Celebrate), trace.IndexOfBeat(LeaderboardBeat.Land));
            Assert.AreEqual(1, trace.CelebrateCount);
            Assert.AreEqual(1, trace.LandedCount);
            AssertLandedAndSettled(trace, "10→2");
        }

        /// <summary>
        /// Bắt đầu ngay ô ranh giới (#4 → #3, #4 → #1): không có gì để leo trong list, nên nhấc lên xong là tới cổng — không pha
        /// Climb, không nhịp Pass trước PodiumTakeover.
        /// </summary>
        [TestCase(3, 2, TestName = "BoundaryStart_4To3_HoldsRightAfterLift")]
        [TestCase(3, 0, TestName = "BoundaryStart_4To1_HoldsRightAfterLift")]
        public void BoundaryStart_HoldsRightAfterLift(int startRank, int targetRank)
        {
            AssertHoldsRightAfterLift(startRank, targetRank, expectedHoldSlot: TopRanks, expectedPresence: 1f);
        }

        /// <summary>
        /// Đã trên bục (#3 → #2, #2 → #1, #3 → #1): đổi chỗ cờ là việc của host — nhấc lên xong là tới cổng, row mình đứng
        /// nguyên ô cũ (sau bục, list không vẽ) cho tới khi host thả.
        /// </summary>
        [TestCase(2, 1, TestName = "PodiumSwap_3To2_HoldsRightAfterLift")]
        [TestCase(1, 0, TestName = "PodiumSwap_2To1_HoldsRightAfterLift")]
        [TestCase(2, 0, TestName = "PodiumSwap_3To1_HoldsRightAfterLift")]
        public void PodiumSwap_HoldsRightAfterLift(int startRank, int targetRank)
        {
            AssertHoldsRightAfterLift(startRank, targetRank, expectedHoldSlot: startRank, expectedPresence: 0f);
        }

        private static void AssertHoldsRightAfterLift(int startRank, int targetRank, float expectedHoldSlot, float expectedPresence)
        {
            BoardScene scene = LoadScene(startRank, targetRank);
            bool isHoldSeen = false;
            RevealTrace trace = RunWithRelease(scene, PodiumSettings(), ReleaseAfterHoldTicks, -1, (run, tick) =>
            {
                if (run.Timeline.Phase != RevealPhase.PodiumHold) return;
                isHoldSeen = true;
                Assert.AreEqual(expectedHoldSlot, run.Model.LocalRow.Slot, 1e-4f, "Row mình phải đứng nguyên ô xuất phát lúc chờ.");
                Assert.AreEqual(expectedPresence, run.Model.ListPresence(run.Model.LocalRow), 1e-4f);
            });

            Assert.IsTrue(trace.Timeline.TakesPodium);
            Assert.IsTrue(isHoldSeen, "Màn diễn chưa bao giờ đứng ở cổng bục.");
            CollectionAssert.DoesNotContain(trace.Phases, RevealPhase.Climb, "Không còn ai để vượt trong list mà vẫn có pha Climb.");
            CollectionAssert.AreEqual(new[] { LeaderboardBeat.RevealStarted, LeaderboardBeat.Lift, LeaderboardBeat.PodiumTakeover },
                                      trace.Beats.GetRange(0, 3), "Phải chờ ngay sau Lift.");
            AssertPhaseOrder(trace, RevealPhase.Lift, RevealPhase.PodiumHold, RevealPhase.Land, RevealPhase.Finished);
            Assert.AreEqual(1, trace.CelebrateCount);
            AssertLandedAndSettled(trace, startRank + "→" + targetRank);
        }

        /// <summary>
        /// Nhảy lớn (ít người được diễn vượt): quay số → leo trong list tới ranh giới → chờ → đi nốt → gắn lại đuôi. Và biến thể
        /// số người diễn được còn ít hơn khoảng cách tới ranh giới (2 lượt, #41 → #1): planner nới đủ để row mình xuất phát Ở ô
        /// ranh giới — quay số xong là tới cổng luôn.
        ///
        /// <para>Cả hai phải dựng được bục "trước" trọn vẹn: lúc RevealStarted, các ô &lt; ranh giới là đúng K người đứng đầu cũ
        /// (hạng hiển thị 0..K-1, không có mình), còn row mình nằm trong list (độ hiện diện 1) — cả lúc bắt đầu lẫn lúc chờ ở cổng.
        /// Trước đây ca 2 lượt để row mình ở ô 2 (sau bục, list không vẽ suốt màn) với số hạng 41, còn người cũ hạng 3 nằm trong
        /// phần đuôi bị tách.</para>
        /// </summary>
        [TestCase(40, 1, 5, true, TestName = "CompressedJump_41To2_SpinsClimbsHoldsAndRevealsTail")]
        [TestCase(40, 0, 2, false, TestName = "CompressedJump_41To1_FewPasses_SpinsThenHolds")]
        public void CompressedJump_IntoPodium(int startRank, int targetRank, int maximumAnimatedPasses, bool expectsListClimb)
        {
            BoardScene scene = LoadScene(startRank, targetRank, maximumAnimatedPasses);
            float holdSlot = -1f;
            float holdPresence = -1f;
            bool isStartStateChecked = false;
            RevealTrace trace = RunWithRelease(scene, PodiumSettings(), ReleaseAfterHoldTicks, -1, (run, tick) =>
            {
                if (tick == 0)
                {
                    AssertBeforePodiumIsTheOldTop(run.Model, "RevealStarted");
                    isStartStateChecked = true;
                }
                if (run.Timeline.Phase != RevealPhase.PodiumHold) return;
                holdSlot = run.Model.LocalRow.Slot;
                holdPresence = run.Model.ListPresence(run.Model.LocalRow);
            });

            Assert.IsTrue(isStartStateChecked);
            Assert.AreEqual(1f, holdPresence, 1e-4f, "Lúc chờ ở cổng row mình phải còn trong list (thanh hạng K+1).");

            Assert.IsTrue(trace.Timeline.Plan.IsCompressed, "Tiền đề hỏng: phải là cú nhảy quay số.");
            Assert.IsTrue(trace.Timeline.TakesPodium);
            CollectionAssert.Contains(trace.Phases, RevealPhase.Spin);
            if (expectsListClimb)
            {
                AssertPhaseOrder(trace, RevealPhase.Spin, RevealPhase.Climb, RevealPhase.PodiumHold, RevealPhase.Land);
                Assert.AreEqual(TopRanks, holdSlot, 1e-4f, "Đoạn trong list phải dừng ở ô ranh giới.");
            }
            else
            {
                CollectionAssert.DoesNotContain(trace.Phases, RevealPhase.Climb);
                AssertPhaseOrder(trace, RevealPhase.Spin, RevealPhase.PodiumHold, RevealPhase.Land);
                Assert.AreEqual(TopRanks, trace.Timeline.Plan.StartSlot, 1e-4f,
                                "Số lượt diễn ít hơn khoảng cách tới ranh giới: planner phải nới để row mình xuất phát ở ô ranh giới.");
                Assert.AreEqual(trace.Timeline.Plan.StartSlot, holdSlot, 1e-4f);
            }
            Assert.AreEqual(trace.Timeline.Plan.Tail.Count > 0 ? 1 : 0, trace.TailRevealedCount, "Đuôi phải được gắn lại sau khi đáp.");
            AssertLandedAndSettled(trace, "compressed");
        }

        /// <summary>
        /// Hạng bằng nhau cắt ngắn dải hạng liền mạch ngay dưới đích (#11 → #1 trên bảng 0, 1, 1, 2, 3...): planner chỉ đếm được
        /// một người liền mạch, nhưng vẫn phải nới tới ranh giới để row mình xuất phát trong list và bục "trước" đủ K người.
        /// </summary>
        [Test]
        public void PodiumPromotion_TiedRanksCutTheContiguousRun_StillStartsInTheList()
        {
            int[] ranks = { 0, 1, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            RankChange change = RankChange.Create(RankChangeKind.RankUp, 10, 0, 90000, 100000);
            BoardScene scene = SyntheticScene(ranks, 0, change);
            bool isStartStateChecked = false;

            RevealTrace trace = RunWithRelease(scene, PodiumSettings(), ReleaseAfterHoldTicks, -1, (run, tick) =>
            {
                if (tick != 0) return;
                Assert.AreEqual(TopRanks, run.Model.HiddenLeadingSlots, "Tiền đề hỏng: hạng bằng nhau không được đẩy ranh giới quá K.");
                // Hạng bằng nhau thì "hạng cũ" của hai người đồng hạng không phân biệt được — chỉ kiểm ai đứng trên bục.
                AssertBeforePodiumIsTheOldTop(run.Model, "RevealStarted", checksDisplayRanks: false);
                isStartStateChecked = true;
            });

            Assert.IsTrue(isStartStateChecked);
            Assert.IsTrue(trace.Timeline.TakesPodium);
            Assert.AreEqual(TopRanks, trace.Timeline.Plan.StartSlot, 1e-4f, "Row mình phải xuất phát ở ô ranh giới, không phải sau bục.");
            Assert.AreEqual(1, trace.CountOf(LeaderboardBeat.PodiumTakeover));
            AssertLandedAndSettled(trace, "tied");
        }

        /// <summary>
        /// Bục "trước" dựng được từ model lúc RevealStarted (host đọc đúng thế): các ô &lt; ranh giới là đúng
        /// <see cref="BoardModel.HiddenLeadingSlots"/> người, không có mình, hạng hiển thị = ô; row mình nằm trong list.
        /// </summary>
        private static void AssertBeforePodiumIsTheOldTop(BoardModel model, string label, bool checksDisplayRanks = true)
        {
            int hidden = model.HiddenLeadingSlots;
            int onPodium = 0;
            IReadOnlyList<RowState> rows = model.Rows;
            for (int index = 0; index < rows.Count; index++)
            {
                RowState row = rows[index];
                if (row.Slot >= hidden) continue;
                onPodium++;
                Assert.IsFalse(row.IsLocalPlayer, label + ": người chơi đến từ list mà lại đứng sau bục (ô " + row.Slot + ").");
                if (checksDisplayRanks)
                {
                    Assert.AreEqual((int)Math.Round(row.Slot), row.DisplayRank, label + ": hạng hiển thị trên bục cũ phải bằng ô.");
                }
            }
            Assert.AreEqual(hidden, onPodium, label + ": bục cũ thiếu người — người cũ hạng K đang nằm ở đâu đó ngoài các ô bục.");
            Assert.AreEqual(1f, model.ListPresence(model.LocalRow), 1e-4f, label + ": row mình phải nằm trong list.");
        }

        // ---------------------------------------------------------------- Đoạn sau cổng bục

        /// <summary>
        /// <see cref="MotionSettings.PodiumClimbDuration"/> = 0: ngay frame thả cổng row mình đã ở ô đích và đang đáp; người bị
        /// vượt ở đoạn này đứng thẳng vào ô mới (không tween — host huỷ proxy đúng frame đó) và không có nhịp Pass nào.
        /// </summary>
        [Test]
        public void PodiumClimbDurationZero_SnapsInTheReleaseFrame()
        {
            BoardScene scene = LoadScene(9, 1);
            var model = new BoardModel(scene, PodiumSettings());
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);
            timeline.Start();

            int ticks = 0;
            while (timeline.Phase != RevealPhase.PodiumHold && ticks++ < MaximumTicks) Step(timeline, model);
            Step(timeline, model);
            Assert.AreEqual(RevealPhase.PodiumHold, timeline.Phase, "Tiền đề hỏng: phải đang chờ ở cổng bục.");
            int passBeatsBeforeRelease = listener.CountOf(LeaderboardBeat.Pass);

            timeline.ReleasePodiumHold();
            timeline.Tick(FrameDeltaTime);

            Assert.AreEqual(RevealPhase.Land, timeline.Phase, "Thả cổng mà chưa đáp ngay trong frame đó.");
            Assert.AreEqual(1f, model.LocalRow.Slot, 1e-4f, "Row mình phải ở ô đích ngay frame thả cổng.");
            RowState oldThird = RowOfRank(model, 3);
            RowState oldSecond = RowOfRank(model, 2);
            Assert.AreEqual(3f, oldThird.Slot, 1e-4f, "Người cũ hạng 3 phải đứng thẳng vào ô 3 (thanh hạng 4) ngay frame thả.");
            Assert.AreEqual(2f, oldSecond.Slot, 1e-4f);
            Assert.IsFalse(oldThird.IsSlotTweening, "PodiumPassSlideDuration = 0 thì không được tween.");
            Assert.AreEqual(passBeatsBeforeRelease, listener.CountOf(LeaderboardBeat.Pass),
                            "Tới đích ngay thì không được dồn một tràng nhịp Pass vào frame thả cổng.");
        }

        /// <summary><see cref="MotionSettings.PodiumClimbDuration"/> &gt; 0: row mình bò từ ranh giới tới đích, người bị vượt trượt theo.</summary>
        [Test]
        public void PodiumClimbDurationPositive_AnimatesTheRemainingClimb()
        {
            const float PodiumClimbDuration = 0.4f;
            BoardScene scene = LoadScene(9, 1);
            int podiumClimbTicks = 0;
            bool isIntermediateSlotSeen = false;
            bool isPassedRowTweenSeen = false;

            RevealTrace trace = RunWithRelease(scene, PodiumSettings(PodiumClimbDuration, 0.2f), ReleaseAfterHoldTicks, -1,
                (run, tick) =>
                {
                    if (run.Timeline.Phase != RevealPhase.PodiumClimb) return;
                    podiumClimbTicks++;
                    float slot = run.Model.LocalRow.Slot;
                    if (slot > 1.01f && slot < TopRanks - 0.01f) isIntermediateSlotSeen = true;
                    if (RowOfRank(run.Model, 3).IsSlotTweening) isPassedRowTweenSeen = true;
                });

            Assert.IsTrue(isIntermediateSlotSeen, "Đoạn sau cổng bục phải đi qua các ô trung gian.");
            Assert.IsTrue(isPassedRowTweenSeen, "Người bị vượt ở đoạn sau cổng phải trượt theo PodiumPassSlideDuration.");
            Assert.AreEqual(PodiumClimbDuration / FrameDeltaTime, podiumClimbTicks, 2f, "Đoạn sau cổng bục phải kéo dài PodiumClimbDuration.");
            Assert.Greater(trace.LastIndexOfBeat(LeaderboardBeat.Pass), trace.IndexOfBeat(LeaderboardBeat.PodiumTakeover),
                           "Đoạn sau cổng có diễn thì người bị vượt ở đó cũng có nhịp Pass.");
            AssertLandedAndSettled(trace, "podium-climb");
        }

        // ---------------------------------------------------------------- Lời thả cổng: tick kế tiếp, bất kể độ dài frame

        /// <summary>
        /// Lời thả cổng bục có hiệu lực ở tick KẾ TIẾP dù frame ngắn hơn nhịp chờ 1/60 s: 120 Hz (kit đặt 120 trên máy ProMotion),
        /// 90 Hz, và 60 Hz mà dt dao động ±4 %. Thả ở 8 vị trí liên tiếp trong lúc chờ (đủ cả chẵn lẻ của nhịp): ngay tick đó row
        /// mình đã ở ô đích và đang đáp, người cũ hạng 3 đã ở ô 3.
        ///
        /// <para>Trước đây lời thả chỉ được đọc khi nhịp chờ hiện tại trôi hết, nên tuỳ pha của nhịp nó trễ thêm một frame — đúng
        /// frame host đã huỷ proxy thanh hạng 4 vì tin model đổi ngay: người cũ hạng 3 còn ở ô 2 (sau bục, không view), thanh hạng 4
        /// biến mất một frame. Test cũ chỉ chạy dt = đúng 1/60 nên không thấy.</para>
        /// </summary>
        [TestCase(120f, 0f, TestName = "PodiumRelease_At120Hz_AppliesOnTheNextTick")]
        [TestCase(90f, 0f, TestName = "PodiumRelease_At90Hz_AppliesOnTheNextTick")]
        [TestCase(60f, 0.04f, TestName = "PodiumRelease_AtJittery60Hz_AppliesOnTheNextTick")]
        public void PodiumRelease_AppliesOnTheNextTick_WhateverTheFrameLength(float frameRate, float jitter)
        {
            for (int ticksIntoHold = 0; ticksIntoHold < 8; ticksIntoHold++)
            {
                string label = frameRate + " Hz, thả sau " + ticksIntoHold + " tick chờ";
                var frames = new FrameClock(frameRate, jitter, 11 + ticksIntoHold);
                var model = new BoardModel(LoadScene(9, 1), PodiumSettings());
                var timeline = new RevealTimeline(model, new RecordingRevealListener());
                timeline.Start();
                for (int tick = 0; tick < MaximumTicks && timeline.Phase != RevealPhase.PodiumHold; tick++) Step(timeline, model, frames.Next());
                for (int tick = 0; tick < ticksIntoHold; tick++) Step(timeline, model, frames.Next());
                Assert.AreEqual(RevealPhase.PodiumHold, timeline.Phase, label + ": tiền đề hỏng — phải đang chờ ở cổng bục.");

                timeline.ReleasePodiumHold();
                timeline.Tick(frames.Next());

                Assert.AreEqual(RevealPhase.Land, timeline.Phase, label + ": thả cổng mà tick kế tiếp chưa đáp.");
                Assert.AreEqual(1f, model.LocalRow.Slot, 1e-4f, label + ": row mình phải ở ô đích ngay tick kế tiếp.");
                Assert.AreEqual(3f, RowOfRank(model, 3).Slot, 1e-4f, label + ": người cũ hạng 3 phải vào ô 3 ngay tick kế tiếp.");
            }
        }

        /// <summary>
        /// Cổng Intro (<see cref="MotionSettings.WaitForHostRelease"/>) cũng vậy: đang ở nhịp chờ thì lời thả có hiệu lực ở tick kế
        /// tiếp, bất kể độ dài frame.
        /// </summary>
        [TestCase(120f, 0f, TestName = "IntroRelease_At120Hz_AppliesOnTheNextTick")]
        [TestCase(60f, 0.04f, TestName = "IntroRelease_AtJittery60Hz_AppliesOnTheNextTick")]
        public void IntroRelease_AppliesOnTheNextTick_WhateverTheFrameLength(float frameRate, float jitter)
        {
            var settings = new MotionSettings { WaitForHostRelease = true, HostHoldTimeout = PatientTimeout };
            for (int ticksIntoHold = 0; ticksIntoHold < 8; ticksIntoHold++)
            {
                string label = frameRate + " Hz, thả sau " + ticksIntoHold + " tick chờ";
                var frames = new FrameClock(frameRate, jitter, 23 + ticksIntoHold);
                var model = new BoardModel(LoadScene(9, 1), settings);
                var timeline = new RevealTimeline(model, new RecordingRevealListener());
                timeline.Start();
                float elapsed = 0f;
                while (elapsed < settings.IntroWait + 0.1f)
                {
                    float deltaTime = frames.Next();
                    Step(timeline, model, deltaTime);
                    elapsed += deltaTime;
                }
                for (int tick = 0; tick < ticksIntoHold; tick++) Step(timeline, model, frames.Next());
                Assert.AreEqual(RevealPhase.Intro, timeline.Phase, label + ": tiền đề hỏng — phải đang chờ host ở Intro.");

                timeline.ReleaseHostHold();
                timeline.Tick(frames.Next());

                Assert.AreEqual(RevealPhase.Lift, timeline.Phase, label + ": thả cổng Intro mà tick kế tiếp chưa sang Lift.");
            }
        }

        /// <summary>
        /// Lời thả chỉ cắt NHỊP CHỜ, không cắt đoạn Intro thật: thả ngay sau Start thì Intro vẫn chạy đủ <c>IntroWait</c> — đúng
        /// hợp đồng "thả trước khi tới cũng được", và đúng hành vi của các game đang thả sớm.
        /// </summary>
        [Test]
        public void IntroRelease_BeforeIntroWaitEnds_StillPlaysTheFullIntro()
        {
            const float DeltaTime = 1f / 120f;
            var settings = new MotionSettings { WaitForHostRelease = true, HostHoldTimeout = PatientTimeout };
            var model = new BoardModel(LoadScene(9, 1), settings);
            var timeline = new RevealTimeline(model, new RecordingRevealListener());
            timeline.Start();
            timeline.ReleaseHostHold();

            int introTicks = 0;
            while (timeline.Phase == RevealPhase.Intro && introTicks < MaximumTicks)
            {
                Step(timeline, model, DeltaTime);
                introTicks++;
            }

            Assert.AreEqual(settings.IntroWait / DeltaTime, introTicks, 1.5f, "Thả sớm không được cắt ngắn đoạn Intro thật.");
        }

        // ---------------------------------------------------------------- Không bao giờ treo

        /// <summary>Thả cổng TRƯỚC khi tới (ngay sau Start, hoặc trước cả Start): tới nơi là đi thẳng, nhịp vẫn phát đúng một lần.</summary>
        [TestCase(true, TestName = "EarlyRelease_BeforeStart_DoesNotHold")]
        [TestCase(false, TestName = "EarlyRelease_AfterStart_DoesNotHold")]
        public void EarlyRelease_DoesNotHold(bool beforeStart)
        {
            BoardScene scene = LoadScene(9, 1);
            var model = new BoardModel(scene, PodiumSettings());
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);
            if (beforeStart) timeline.ReleasePodiumHold();
            timeline.Start();
            if (!beforeStart) timeline.ReleasePodiumHold();

            for (int tick = 0; tick < MaximumTicks && !timeline.IsFinished; tick++)
            {
                Step(timeline, model);
                Assert.AreNotEqual(RevealPhase.PodiumHold, timeline.Phase, "Tick " + tick + ": đã thả sớm mà vẫn đứng chờ.");
            }

            Assert.IsTrue(timeline.IsFinished);
            Assert.AreEqual(1, listener.CountOf(LeaderboardBeat.PodiumTakeover), "Thả sớm vẫn phải báo host đúng một lần.");
            Assert.IsFalse(timeline.PodiumHoldTimedOut);
        }

        /// <summary>Host không bao giờ thả: sau <see cref="MotionSettings.HostHoldTimeout"/> màn diễn tự đi tiếp và ĐẶT CỜ.</summary>
        [Test]
        public void PodiumHold_TimesOut_FlagsItAndFinishes()
        {
            const float Timeout = 0.5f;
            RevealTrace trace = RunWithRelease(LoadScene(9, 1), PodiumSettings(holdTimeout: Timeout), releaseAfterHoldTicks: -1);

            int holdFrames = 0;
            for (int index = 0; index < trace.Phases.Count; index++)
            {
                if (trace.Phases[index] == RevealPhase.PodiumHold) holdFrames++;
            }

            Assert.IsTrue(trace.Timeline.PodiumHoldTimedOut, "Hết giờ ở cổng bục mà không đặt cờ — lớp UI sẽ không có gì để kêu.");
            Assert.IsFalse(trace.Timeline.HostHoldTimedOut, "Cổng Intro không bật mà lại báo hết giờ.");
            Assert.AreEqual(Timeout / FrameDeltaTime, holdFrames, 3f, "Phải chờ đúng HostHoldTimeout rồi mới đi tiếp.");
            AssertLandedAndSettled(trace, "timeout");
        }

        /// <summary>
        /// Bỏ qua ở MỌI tick — trước khi tới cổng, lúc đang chờ (cả khi host không bao giờ thả), lúc đi nốt, lúc đáp: luôn ra
        /// Skipped (nếu chưa đáp) → Land → Celebrate → RevealFinished, trạng thái cuối, không treo.
        /// </summary>
        [TestCase(9, 1, ReleaseAfterHoldTicks, TestName = "Podium_SkipAtEveryTick_10To2")]
        [TestCase(2, 1, ReleaseAfterHoldTicks, TestName = "Podium_SkipAtEveryTick_3To2")]
        [TestCase(3, 0, ReleaseAfterHoldTicks, TestName = "Podium_SkipAtEveryTick_4To1")]
        [TestCase(9, 1, -1, TestName = "Podium_SkipAtEveryTick_10To2_HostNeverReleases")]
        public void Podium_SkipAtEveryTick_SettlesFinalState(int startRank, int targetRank, int releaseAfterHoldTicks)
        {
            MotionSettings settings = PodiumSettings(podiumClimbDuration: 0.3f, podiumPassSlideDuration: 0.15f);
            RevealTrace reference = RunWithRelease(LoadScene(startRank, targetRank), settings,
                                                   releaseAfterHoldTicks >= 0 ? releaseAfterHoldTicks : ReleaseAfterHoldTicks);
            int totalTicks = reference.Phases.Count;
            int firstHoldFrame = reference.Phases.IndexOf(RevealPhase.PodiumHold);
            Assert.Greater(firstHoldFrame, 0, "Tiền đề hỏng: bản tham chiếu không đứng ở cổng bục.");

            for (int skipTick = 0; skipTick <= totalTicks; skipTick++)
            {
                RevealTrace run = RunWithRelease(LoadScene(startRank, targetRank), settings, releaseAfterHoldTicks, skipTick);
                string label = "skip@" + skipTick;
                bool skippedBeforeLanding = run.CountOf(LeaderboardBeat.Skipped) == 1;

                AssertLandedAndSettled(run, label);
                Assert.IsFalse(run.Timeline.PodiumHoldTimedOut, label + ": bỏ qua phải thả cổng, không được chờ tới hết giờ.");
                Assert.AreEqual(1, run.CelebrateCount, label + ": đúng 1 lần ăn mừng.");
                Assert.AreEqual(1, run.CountOf(LeaderboardBeat.Celebrate), label);
                Assert.LessOrEqual(run.CountOf(LeaderboardBeat.Skipped), 1, label);
                if (skippedBeforeLanding)
                {
                    Assert.Less(run.IndexOfBeat(LeaderboardBeat.Skipped), run.IndexOfBeat(LeaderboardBeat.Land), label);
                    Assert.IsTrue(run.Timeline.WasSkipped, label);
                }
                // Bỏ qua trước khi tới cổng (skipTick là số tick đã chạy trước lượt bỏ qua; frame 0 là trạng thái sau Start).
                if (skipTick < firstHoldFrame - 1)
                {
                    Assert.AreEqual(0, run.CountOf(LeaderboardBeat.PodiumTakeover), label + ": bỏ qua trước cổng thì không trao quyền.");
                }
            }
        }

        /// <summary>Đóng giữa lúc đang chờ ở cổng: dựng trạng thái cuối, không phát nhịp nào, không coi là đã đáp.</summary>
        [Test]
        public void ForceFinish_DuringPodiumHold_SettlesWithoutBeats()
        {
            BoardScene scene = LoadScene(9, 1);
            var model = new BoardModel(scene, PodiumSettings());
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);
            timeline.Start();
            int ticks = 0;
            while (timeline.Phase != RevealPhase.PodiumHold && ticks++ < MaximumTicks) Step(timeline, model);
            for (int tick = 0; tick < 10; tick++) Step(timeline, model);
            Assert.AreEqual(RevealPhase.PodiumHold, timeline.Phase, "Tiền đề hỏng: phải đang chờ ở cổng bục.");
            int beatsBefore = listener.Beats.Count;

            timeline.ForceFinish();

            Assert.IsTrue(timeline.IsFinished);
            Assert.IsFalse(timeline.HasReachedLanding, "Đóng lúc chờ cổng bục thì lần mở sau phải diễn lại.");
            Assert.AreEqual(beatsBefore, listener.Beats.Count, "ForceFinish không được phát nhịp.");
            RankUpPlannerInvariantTests.AssertRowsSettled(model, "force-finish-in-hold");
        }

        /// <summary>
        /// Hai cổng (Intro — <see cref="MotionSettings.WaitForHostRelease"/> — và cổng bục) độc lập: thả cổng này không mở cổng
        /// kia, và thả sớm cổng bục trong lúc còn kẹt ở Intro vẫn có hiệu lực khi tới nơi.
        /// </summary>
        [Test]
        public void IntroHoldAndPodiumHold_AreIndependentGates()
        {
            MotionSettings settings = PodiumSettings();
            settings.WaitForHostRelease = true;

            // Thả cổng bục trước: vẫn kẹt ở Intro.
            var model = new BoardModel(LoadScene(9, 1), settings);
            var listener = new RecordingRevealListener();
            var timeline = new RevealTimeline(model, listener);
            timeline.Start();
            timeline.ReleasePodiumHold();
            for (int tick = 0; tick < 120; tick++) Step(timeline, model);
            Assert.AreEqual(RevealPhase.Intro, timeline.Phase, "Thả cổng bục đã mở luôn cổng Intro.");

            timeline.ReleaseHostHold();
            for (int tick = 0; tick < MaximumTicks && !timeline.IsFinished; tick++)
            {
                Step(timeline, model);
                Assert.AreNotEqual(RevealPhase.PodiumHold, timeline.Phase, "Cổng bục đã thả sớm mà vẫn đứng chờ.");
            }
            Assert.IsTrue(timeline.IsFinished);
            Assert.AreEqual(1, listener.CountOf(LeaderboardBeat.PodiumTakeover));

            // Thả cổng Intro: tới cổng bục vẫn phải đứng chờ.
            model = new BoardModel(LoadScene(9, 1), settings);
            listener = new RecordingRevealListener();
            timeline = new RevealTimeline(model, listener);
            timeline.Start();
            timeline.ReleaseHostHold();
            for (int tick = 0; tick < 240; tick++) Step(timeline, model);
            Assert.AreEqual(RevealPhase.PodiumHold, timeline.Phase, "Thả cổng Intro đã mở luôn cổng bục.");

            timeline.ReleasePodiumHold();
            for (int tick = 0; tick < MaximumTicks && !timeline.IsFinished; tick++) Step(timeline, model);
            Assert.IsTrue(timeline.IsFinished);
            Assert.IsFalse(timeline.HostHoldTimedOut);
            Assert.IsFalse(timeline.PodiumHoldTimedOut);
        }

        // ---------------------------------------------------------------- Không lên bục kiểu này

        /// <summary>NEW vào top 3, không đổi hạng khi đang trên bục: không có cổng bục — host dựng thẳng trạng thái của chúng.</summary>
        [TestCase(-1, 1, RankChangeKind.NewEntry, TestName = "NewEntryIntoPodium_HasNoPodiumHold")]
        [TestCase(1, 1, RankChangeKind.Unchanged, TestName = "UnchangedOnPodium_HasNoPodiumHold")]
        public void NonRankUpOnPodium_HasNoPodiumHold(int startRank, int targetRank, RankChangeKind expectedKind)
        {
            RevealTrace trace = RunWithRelease(LoadScene(startRank, targetRank), PodiumSettings(), releaseAfterHoldTicks: -1);

            Assert.AreEqual(expectedKind, trace.Model.Scene.Change.Kind, "Tiền đề hỏng: sai loại thay đổi.");
            AssertNoPodiumHold(trace);
        }

        /// <summary>Tụt khỏi bục (#2 → #6) hoặc tụt trong bục (#1 → #3): không có kế hoạch tua ngược, không có cổng bục.</summary>
        [TestCase(1, 5, TestName = "RankDownOutOfPodium_HasNoPodiumHold")]
        [TestCase(0, 2, TestName = "RankDownWithinPodium_HasNoPodiumHold")]
        public void RankDown_HasNoPodiumHold(int fromRank, int toRank)
        {
            RankChange change = RankChange.Create(RankChangeKind.RankDown, fromRank, toRank, 5000, 5000);
            BoardScene scene = SyntheticScene(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, toRank, change);

            RevealTrace trace = RunWithRelease(scene, PodiumSettings(), releaseAfterHoldTicks: -1);

            AssertNoPodiumHold(trace);
        }

        private static void AssertNoPodiumHold(RevealTrace trace)
        {
            Assert.IsTrue(trace.Timeline.IsFinished, "Màn diễn phải tự kết thúc — không có ai thả cổng bục.");
            Assert.IsFalse(trace.Timeline.TakesPodium);
            Assert.AreEqual(0, trace.CountOf(LeaderboardBeat.PodiumTakeover));
            CollectionAssert.DoesNotContain(trace.Phases, RevealPhase.PodiumHold);
            Assert.IsFalse(trace.Timeline.PodiumHoldTimedOut);
        }

        // ---------------------------------------------------------------- BoardModel

        [TestCase(new[] { 0, 1, 2, 3, 4 }, 3, 3, TestName = "HiddenLeadingSlots_FullTop_CountsK")]
        [TestCase(new[] { 0, -1, 7, 8, 9 }, 3, 1, TestName = "HiddenLeadingSlots_GapAfterFirst_StopsAtGap")]
        [TestCase(new[] { 0, 1, 1, 2, 3 }, 3, 3, TestName = "HiddenLeadingSlots_TiedRanks_NeverExceedsK")]
        [TestCase(new[] { 0, 0, 0, 0, 1 }, 3, 3, TestName = "HiddenLeadingSlots_AllTiedAtTheTop_NeverExceedsK")]
        [TestCase(new[] { 0, 1, -1, 20, 21 }, 3, 2, TestName = "HiddenLeadingSlots_TopCountBelowK_CountsWhatIsLoaded")]
        [TestCase(new[] { 5, 6, 7 }, 3, 0, TestName = "HiddenLeadingSlots_NoTopLoaded_IsZero")]
        [TestCase(new[] { 0, 1, 2, 3 }, 0, 0, TestName = "HiddenLeadingSlots_FlagOff_IsZero")]
        public void HiddenLeadingSlots_CountsLeadingRowsBelowK(int[] ranks, int topRanks, int expected)
        {
            int localIndex = Array.FindLastIndex(ranks, rank => rank >= 0);
            RankChange change = RankChange.Create(RankChangeKind.Unchanged, ranks[localIndex], ranks[localIndex], 100, 100);
            var model = new BoardModel(SyntheticScene(ranks, localIndex, change), new MotionSettings { HostPresentedTopRanks = topRanks });

            Assert.AreEqual(expected, model.HiddenLeadingSlots);
        }

        /// <summary>
        /// Hạng bằng nhau (0, 1, 1, 2 với bục 3 cờ): người đồng hạng thứ K+1 ở lại list — ô 3 có độ hiện diện 1, không ai "đáp lên
        /// bục" từ ô đó. Trước đây ranh giới thành 4: list giấu 4 row trong khi bục chỉ có 3 cờ, người thứ tư không ai vẽ.
        /// </summary>
        [Test]
        public void HiddenLeadingSlots_TiedRanks_ExtraRowStaysInTheList()
        {
            BoardModel model = SyntheticModel(new[] { 0, 1, 1, 2, 3, 4 }, localIndex: 3, topRanks: TopRanks);

            Assert.AreEqual(TopRanks, model.HiddenLeadingSlots);
            Assert.AreEqual(1f, model.ListPresence(model.Rows[3]), "Người đồng hạng thứ K+1 phải được list vẽ.");
            Assert.IsFalse(model.LocalLandsOnPodium, "Ô 3 là ô của list, không phải của bục.");
        }

        [TestCase(0f, 0f)]
        [TestCase(2f, 0f)]
        [TestCase(2.25f, 0.25f)]
        [TestCase(2.5f, 0.5f)]
        [TestCase(3f, 1f)]
        [TestCase(12f, 1f)]
        public void ListPresence_CrossfadesBetweenPodiumAndList(float slot, float expected)
        {
            BoardModel model = SyntheticModel(new[] { 0, 1, 2, 3, 4, 5 }, localIndex: 4, topRanks: TopRanks);
            RowState row = model.Rows[5];
            row.Slot = slot;

            Assert.AreEqual(expected, model.ListPresence(row), 1e-5f);
            Assert.AreEqual(expected < 1f, model.IsPresentedByHost(row));
        }

        [Test]
        public void ListPresence_FlagOff_IsAlwaysOne_ButHiddenFromListWins()
        {
            BoardModel model = SyntheticModel(new[] { 0, 1, 2, 3 }, localIndex: 1, topRanks: 0);

            Assert.AreEqual(1f, model.ListPresence(model.Rows[0]));
            Assert.IsFalse(model.IsPresentedByHost(model.Rows[0]));
            Assert.IsFalse(model.LocalLandsOnPodium, "Cờ tắt thì không ai 'đáp lên bục'.");

            model.Rows[2].IsHiddenFromList = true;
            Assert.AreEqual(0f, model.ListPresence(model.Rows[2]), "Host giành row khỏi list thì độ hiện diện phải là 0.");
            Assert.IsTrue(model.IsPresentedByHost(model.Rows[2]));
            Assert.AreEqual(0f, model.ListPresence(null));
        }

        [TestCase(0, true)]
        [TestCase(2, true)]
        [TestCase(3, false)]
        [TestCase(5, false)]
        public void LocalLandsOnPodium_FollowsTheFinalIndex(int localIndex, bool expected)
        {
            BoardModel model = SyntheticModel(new[] { 0, 1, 2, 3, 4, 5 }, localIndex, TopRanks);
            model.LocalRow.Slot = 20f;   // Slot đang chạy không liên quan — chỉ ô CUỐI quyết định.

            Assert.AreEqual(expected, model.LocalLandsOnPodium);
        }

        /// <summary>Widget chụp settings bằng <c>Clone()</c>: ba field mới phải đi theo, không thì cờ bật trong asset không tới được timeline.</summary>
        [Test]
        public void Clone_CarriesThePodiumSettings()
        {
            var settings = new MotionSettings { HostPresentedTopRanks = 3, PodiumClimbDuration = 0.25f, PodiumPassSlideDuration = 0.1f };

            MotionSettings copy = settings.Clone();

            Assert.AreEqual(3, copy.HostPresentedTopRanks, "Clone() đánh rơi HostPresentedTopRanks.");
            Assert.AreEqual(0.25f, copy.PodiumClimbDuration, "Clone() đánh rơi PodiumClimbDuration.");
            Assert.AreEqual(0.1f, copy.PodiumPassSlideDuration, "Clone() đánh rơi PodiumPassSlideDuration.");
        }

        /// <summary>Mặc định phải là TẮT — asset cũ thiếu field sẽ nhận đúng các giá trị này.</summary>
        [Test]
        public void Defaults_AreOff()
        {
            var settings = new MotionSettings();

            Assert.AreEqual(0, settings.HostPresentedTopRanks);
            Assert.AreEqual(0f, settings.PodiumClimbDuration);
            Assert.AreEqual(0f, settings.PodiumPassSlideDuration);
            Assert.IsFalse(new RowState(BoardRow.ForEntry(new LeaderboardEntry("p", "P", 1, 0), false), 0f).IsHiddenFromList);
        }

        // ---------------------------------------------------------------- Tiện ích

        private static void Step(RevealTimeline timeline, BoardModel model)
        {
            Step(timeline, model, FrameDeltaTime);
        }

        private static void Step(RevealTimeline timeline, BoardModel model, float deltaTime)
        {
            timeline.Tick(deltaTime);
            model.Advance(deltaTime);
        }

        /// <summary>
        /// Dãy dt của một máy chạy <c>frameRate</c> Hz, dao động ngẫu nhiên ±<c>jitter</c> (tỉ lệ) quanh 1/frameRate — tất định
        /// theo seed để test đỏ thì chạy lại vẫn đỏ đúng chỗ đó.
        /// </summary>
        private sealed class FrameClock
        {
            private readonly float _frameDuration;
            private readonly float _jitter;
            private readonly Random _random;

            public FrameClock(float frameRate, float jitter, int seed)
            {
                _frameDuration = 1f / frameRate;
                _jitter = jitter;
                _random = new Random(seed);
            }

            public float Next()
            {
                return _frameDuration * (1f + _jitter * (float)(_random.NextDouble() * 2.0 - 1.0));
            }
        }

        /// <summary>Các pha xuất hiện (ở cuối một tick nào đó) đúng theo thứ tự này.</summary>
        private static void AssertPhaseOrder(RevealTrace trace, params RevealPhase[] phases)
        {
            int previous = -1;
            for (int index = 0; index < phases.Length; index++)
            {
                int first = trace.Phases.IndexOf(phases[index]);
                Assert.GreaterOrEqual(first, 0, "Không thấy pha " + phases[index] + ".");
                Assert.Greater(first, previous, "Pha " + phases[index] + " xuất hiện sai thứ tự.");
                previous = first;
            }
        }

        /// <summary>Bảng dựng tay: <paramref name="ranks"/> theo thứ tự dòng, -1 = "...".</summary>
        internal static BoardScene SyntheticScene(int[] ranks, int localIndex, RankChange change)
        {
            var rows = new List<BoardRow>(ranks.Length);
            for (int index = 0; index < ranks.Length; index++)
            {
                int rank = ranks[index];
                rows.Add(rank < 0
                    ? BoardRow.Gap()
                    : BoardRow.ForEntry(new LeaderboardEntry("player-" + index, "Player " + index, 100000 - rank * 100, rank),
                                        index == localIndex));
            }
            return new BoardScene("synthetic", rows, localIndex, change, RankTierRule.Default,
                                  FetchWindowPlanner.Plan(change, FetchWindowSettings.Default), BoardPresentMode.RevealIfPending);
        }

        private static BoardModel SyntheticModel(int[] ranks, int localIndex, int topRanks)
        {
            RankChange change = RankChange.Create(RankChangeKind.Unchanged, ranks[localIndex], ranks[localIndex], 100, 100);
            return new BoardModel(SyntheticScene(ranks, localIndex, change), new MotionSettings { HostPresentedTopRanks = topRanks });
        }
    }
}
