using System;
using DreamTech.Leaderboard.ViewModel;
using NUnit.Framework;

namespace DreamTech.Leaderboard.Tests
{
    [TestFixture]
    public class RowStateEffectTests
    {
        private static readonly MotionSettings Settings = new MotionSettings();

        private static RowState CreateRow(int rank)
        {
            return new RowState(BoardRow.ForEntry(new LeaderboardEntry("p", "P", 100, rank), false), 0f);
        }

        [Test]
        public void SetDisplayRank_Animate_StartsRollWithImprovingDirection()
        {
            RowState row = CreateRow(10);

            row.SetDisplayRank(9, 1.0, Settings, RankTierRule.Default, true);

            Assert.IsTrue(row.RankRollTiming.IsRunning(1.0));
            Assert.AreEqual(10, row.RankRollPreviousRank);
            Assert.AreEqual(1, row.RankRollDirection);
        }

        [Test]
        public void SetDisplayRank_WithinThrottleWindow_DoesNotRestartRoll()
        {
            RowState row = CreateRow(10);
            row.SetDisplayRank(9, 1.0, Settings, RankTierRule.Default, true);

            row.SetDisplayRank(8, 1.0 + Settings.RankRollDuration * 0.5, Settings, RankTierRule.Default, true);

            Assert.AreEqual(1.0, row.RankRollTiming.StartTime, 1e-9);
            Assert.AreEqual(8, row.DisplayRank);
        }

        [Test]
        public void SetDisplayRank_EnteringMedalTier_PunchesBadge()
        {
            RowState row = CreateRow(4);

            row.SetDisplayRank(3, 0.0, Settings, RankTierRule.Default, true);
            Assert.IsFalse(row.BadgePunchTiming.IsStarted, "Hạng 4 (0-based 3) chưa có huy chương");

            row.SetDisplayRank(2, 1.0, Settings, RankTierRule.Default, true);
            Assert.IsTrue(row.BadgePunchTiming.IsRunning(1.0));
        }

        [Test]
        public void SetDisplayRankImmediate_StopsRollAndBumpsVersion()
        {
            RowState row = CreateRow(10);
            row.SetDisplayRank(9, 0.0, Settings, RankTierRule.Default, true);
            int version = row.ContentVersion;

            row.SetDisplayRankImmediate(5);

            Assert.IsFalse(row.RankRollTiming.IsStarted);
            Assert.Greater(row.ContentVersion, version);
        }

        [Test]
        public void Pill_ProgressFollowsClock_AndEndsAfterTotalDuration()
        {
            RowState row = CreateRow(10);
            row.StartPill(PillContent.RankUp, 12, RankTier.Standard, 2.0, Settings);

            Assert.IsTrue(row.PillTiming.IsRunning(2.0 + Settings.PillPopDuration));
            Assert.IsFalse(row.PillTiming.IsRunning(2.0 + Settings.PillTotalDuration + 0.01));
            Assert.AreEqual(12, row.PillValue);
        }

        [Test]
        public void TweenSlot_FinishTweens_SnapsToTarget()
        {
            RowState row = CreateRow(10);
            row.TweenSlot(3f, 0.26f);
            row.Advance(0.05f, Settings);
            Assert.AreEqual(3f, row.TargetSlot);
            Assert.Less(row.Slot, 3f);

            row.FinishTweens();

            Assert.AreEqual(3f, row.Slot);
            Assert.IsFalse(row.IsSlotTweening);
        }

        [Test]
        public void Intro_StartsInvisible_EndsFullyVisible()
        {
            RowState row = CreateRow(10);
            row.StartIntro(0.1f);
            Assert.AreEqual(0f, row.IntroAlpha);

            for (int tick = 0; tick < 60; tick++) row.Advance(1f / 60f, Settings);

            Assert.AreEqual(1f, row.IntroAlpha);
            Assert.AreEqual(0f, row.IntroOffset);
            Assert.AreEqual(1f, row.IntroScale);
        }
    }

    [TestFixture]
    public class BannerPlacementTests
    {
        [Test]
        public void Resolve_RoomBelow_PlacesBelowRow()
        {
            BannerPlacement placement = BannerPlacement.Resolve(100f, 224f, 200f, 0f, 1200f, 16f);

            Assert.AreEqual(BannerSide.Below, placement.Side);
            Assert.AreEqual(224f + 16f + 100f, placement.CenterY, 1e-3f);
        }

        [Test]
        public void Resolve_NoRoomBelow_PlacesAboveRow()
        {
            BannerPlacement placement = BannerPlacement.Resolve(900f, 1024f, 200f, 0f, 1100f, 16f);

            Assert.AreEqual(BannerSide.Above, placement.Side);
            Assert.AreEqual(900f - 16f - 100f, placement.CenterY, 1e-3f);
        }

        [Test]
        public void Resolve_NeitherFits_ClampsInsideContainer_OnWiderSide()
        {
            BannerPlacement placement = BannerPlacement.Resolve(150f, 274f, 200f, 0f, 400f, 16f);

            Assert.AreEqual(BannerSide.Above, placement.Side);
            Assert.GreaterOrEqual(placement.CenterY - 100f, 0f);
            Assert.LessOrEqual(placement.CenterY + 100f, 400f);
        }

        [Test]
        public void Resolve_BelowPlacement_NeverOverlapsRow()
        {
            BannerPlacement placement = BannerPlacement.Resolve(40f, 164f, 200f, 0f, 1300f, 12f);
            Assert.GreaterOrEqual(placement.CenterY - 100f, 164f);
        }
    }

    [TestFixture]
    public class VirtualListLayoutTests
    {
        private static readonly VirtualListLayout Layout = new VirtualListLayout(124f, 12f, 40f, 56f);

        [Test]
        public void ContentHeight_NoRows_IsZero()
        {
            Assert.AreEqual(0f, Layout.ContentHeight(-1f));
        }

        [Test]
        public void ContentHeight_TenRows_IncludesPaddingWithoutTrailingSpacing()
        {
            Assert.AreEqual(40f + 10 * 136f - 12f + 56f, Layout.ContentHeight(9f), 1e-3f);
        }

        [Test]
        public void CenteredScroll_IsClampedToRange()
        {
            float maximumScroll = Layout.MaximumScroll(Layout.ContentHeight(99f), 1000f);
            Assert.AreEqual(0f, Layout.CenteredScroll(0f, 1000f, maximumScroll));
            Assert.AreEqual(maximumScroll, Layout.CenteredScroll(99f, 1000f, maximumScroll));
        }

        [Test]
        public void Intersects_RowJustOutsideBand_IsFalse()
        {
            float top = Layout.SlotToTop(5f);
            Assert.IsTrue(Layout.Intersects(5f, top + 124f, top + 500f));
            Assert.IsFalse(Layout.Intersects(5f, top + 124.5f, top + 500f));
        }

        // ------------------------------------------------------------ Divider (dải zone của League)

        private static readonly VirtualListLayout Divided =
            new VirtualListLayout(124f, 12f, 40f, 56f, new[] { new ListDivider(5, 120f), new ListDivider(25, 80f) });

        [Test]
        public void Divider_PushesRowsBelow_LeavesRowsAboveUntouched()
        {
            Assert.AreEqual(Layout.SlotToTop(4f), Divided.SlotToTop(4f), 1e-3f);
            Assert.AreEqual(Layout.SlotToTop(5f) + 120f, Divided.SlotToTop(5f), 1e-3f);
            Assert.AreEqual(Layout.SlotToTop(29f) + 200f, Divided.SlotToTop(29f), 1e-3f);
            Assert.AreEqual(Layout.ContentHeight(29f) + 200f, Divided.ContentHeight(29f), 1e-3f);
        }

        [Test]
        public void Divider_SitsFlushAboveItsRow_AfterRowAboveAndSpacing()
        {
            Assert.AreEqual(Divided.SlotToTop(4f) + 124f + 12f, Divided.DividerTop(0), 1e-3f);
            Assert.AreEqual(Divided.SlotToTop(5f), Divided.DividerTop(0) + 120f, 1e-3f);
        }

        [Test]
        public void Divider_RowCrossingIt_MovesContinuously()
        {
            // Row đang leo đi qua dải không được giật: bước slot nhỏ thì vị trí cũng chỉ đổi nhỏ.
            float previous = Divided.SlotToTop(6f);
            for (float slot = 6f; slot >= 3f; slot -= 0.01f)
            {
                float top = Divided.SlotToTop(slot);
                Assert.LessOrEqual(previous - top, (136f + 120f) * 0.01f + 1e-2f, "slot " + slot);
                Assert.LessOrEqual(top, previous + 1e-3f, "Phải đơn điệu");
                previous = top;
            }
        }

        [Test]
        public void Divider_TopVisibleSlot_IsInverseOfSlotToTop()
        {
            for (float slot = -0.5f; slot <= 29f; slot += 0.125f)
            {
                Assert.AreEqual(slot, Divided.TopVisibleSlot(Divided.SlotToTop(slot)), 1e-3f, "slot " + slot);
            }
            for (float slot = 0f; slot <= 29f; slot += 0.5f)
            {
                Assert.AreEqual(Layout.TopVisibleSlot(Layout.SlotToTop(slot)), slot, 1e-3f);
            }
        }

        [Test]
        public void Divider_AdjacentSlots_StillInvertible()
        {
            var adjacent = new VirtualListLayout(100f, 10f, 0f, 0f, new[] { new ListDivider(3, 40f), new ListDivider(4, 60f) });
            for (float slot = 0f; slot <= 8f; slot += 0.1f)
            {
                Assert.AreEqual(slot, adjacent.TopVisibleSlot(adjacent.SlotToTop(slot)), 1e-3f, "slot " + slot);
            }
            Assert.AreEqual(adjacent.SlotToTop(3f), adjacent.DividerTop(0) + 40f, 1e-3f);
            Assert.AreEqual(adjacent.SlotToTop(4f), adjacent.DividerTop(1) + 60f, 1e-3f);
        }
    }

    [TestFixture]
    public class MotionMathTests
    {
        [Test]
        public void DampedSpring_ImpulsesAndFrameSpikes_BarelyOvershootAndSettle()
        {
            var spring = new DampedSpring();
            spring.Reset(1f);
            spring.Target = 1.06f;
            float peak = 0f;
            var random = new Random(1);
            for (int index = 0; index < 600; index++)
            {
                float deltaTime = index % 50 == 0 ? 0.25f : (float)(0.008 + random.NextDouble() * 0.03);
                spring.Step(deltaTime, 300f, 22f);
                peak = Math.Max(peak, spring.Value);
                if (index == 100) spring.Target = 1f;
            }

            Assert.Less(peak, 1.075f);
            Assert.IsTrue(spring.IsSettled);
        }

        [Test]
        public void SmoothDamping_MatchesUnityMathfSmoothDamp()
        {
            var random = new Random(3);
            for (int index = 0; index < 500; index++)
            {
                float current = (float)(random.NextDouble() * 2000 - 1000);
                float target = (float)(random.NextDouble() * 2000 - 1000);
                float velocity = (float)(random.NextDouble() * 400 - 200);
                float smoothTime = (float)(0.01 + random.NextDouble());
                float deltaTime = (float)(0.001 + random.NextDouble() * 0.1);

                float expectedVelocity = velocity;
                float expected = UnityEngine.Mathf.SmoothDamp(current, target, ref expectedVelocity, smoothTime, float.PositiveInfinity, deltaTime);
                float actualVelocity = velocity;
                float actual = SmoothDamping.Step(current, target, ref actualVelocity, smoothTime, float.PositiveInfinity, deltaTime);

                Assert.AreEqual(expected, actual, 1e-2f);
                Assert.AreEqual(expectedVelocity, actualVelocity, 1e-2f);
            }
        }

        [Test]
        public void Easing_EndpointsAreExact()
        {
            Assert.AreEqual(0f, Easing.OutCubic(0f));
            Assert.AreEqual(1f, Easing.OutCubic(1f));
            Assert.AreEqual(0f, Easing.InOutCubic(0f));
            Assert.AreEqual(1f, Easing.InOutCubic(1f), 1e-6f);
            Assert.AreEqual(1f, Easing.OutBack(1f, 1.3f), 1e-6f);
            Assert.AreEqual(250L, Easing.LerpLong(200, 300, 0.5f));
        }
    }
}
