using System;
using System.Linq;
using NUnit.Framework;

namespace DreamTech.Leaderboard.League.Tests
{
    [TestFixture]
    public class LeagueLadderTests
    {
        [Test]
        public void Constructor_RejectsEmptyAndDuplicateTiers()
        {
            Assert.Throws<ArgumentException>(() => new LeagueLadder(Array.Empty<LeagueTierDefinition>()));
            Assert.Throws<ArgumentException>(() => new LeagueLadder(new[]
            {
                new LeagueTierDefinition("bronze", 1, 1),
                new LeagueTierDefinition("bronze", 1, 1),
            }));
        }

        [Test]
        public void IndexOf_ClampIndex_TopAndBottom()
        {
            LeagueLadder ladder = LeagueTestFactory.CreateLadder();
            Assert.AreEqual(2, ladder.IndexOf("gold"));
            Assert.AreEqual(-1, ladder.IndexOf("mythic"));
            Assert.AreEqual(0, ladder.ClampIndex(-3));
            Assert.AreEqual(4, ladder.ClampIndex(9));
            Assert.IsTrue(ladder.IsBottom(0));
            Assert.IsTrue(ladder.IsTop(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => ladder.TierAt(5));
        }
    }

    [TestFixture]
    public class LeagueZoneTests
    {
        [Test]
        public void ZoneOf_Boundaries()
        {
            var bands = new LeagueZoneBands(30, 5, 5);
            Assert.AreEqual(LeagueZone.Promotion, bands.ZoneOf(0));
            Assert.AreEqual(LeagueZone.Promotion, bands.ZoneOf(4));
            Assert.AreEqual(LeagueZone.Safe, bands.ZoneOf(5));
            Assert.AreEqual(LeagueZone.Safe, bands.ZoneOf(24));
            Assert.AreEqual(LeagueZone.Demotion, bands.ZoneOf(25));
            Assert.AreEqual(LeagueZone.Demotion, bands.ZoneOf(29));
            Assert.AreEqual(LeagueZone.Safe, bands.ZoneOf(-1));
            Assert.AreEqual(LeagueZone.Safe, bands.ZoneOf(30));
            Assert.AreEqual(5, bands.PromotionEndRank);
            Assert.AreEqual(25, bands.DemotionStartRank);
        }

        [Test]
        public void SmallGroup_KeepsPromotion_ShrinksDemotion()
        {
            var bands = new LeagueZoneBands(6, 5, 5);
            Assert.AreEqual(5, bands.PromotionCount);
            Assert.AreEqual(1, bands.DemotionCount);
            Assert.AreEqual(LeagueZone.Promotion, bands.ZoneOf(4));
            Assert.AreEqual(LeagueZone.Demotion, bands.ZoneOf(5));
        }

        [Test]
        public void CountRule_BottomTierHasNoDemotion_TopTierHasNoPromotion()
        {
            LeagueLadder ladder = LeagueTestFactory.CreateLadder();
            var rule = new CountLeagueZoneRule();

            LeagueZoneBands bronze = rule.GetBands(ladder, 0, 30);
            Assert.IsTrue(bronze.HasPromotion);
            Assert.IsFalse(bronze.HasDemotion);

            LeagueZoneBands diamond = rule.GetBands(ladder, 4, 30);
            Assert.IsFalse(diamond.HasPromotion);
            Assert.IsTrue(diamond.HasDemotion);

            LeagueZoneBands silver = rule.GetBands(ladder, 1, 30);
            Assert.IsTrue(silver.HasPromotion && silver.HasDemotion);
        }
    }

    [TestFixture]
    public class SeasonOutcomeRuleTests
    {
        private static SeasonOutcomeDecision Decide(ISeasonOutcomeRule rule, int tier, int rank, bool participated = true)
        {
            var input = new SeasonOutcomeInput(tier, rank, 30, participated ? 50 : 0, participated);
            return rule.Decide(input, LeagueTestFactory.CreateLadder(), new CountLeagueZoneRule());
        }

        [Test]
        public void PromotionZone_Promotes_DemotionZone_Demotes_Safe_Stays()
        {
            var rule = new ZoneSeasonOutcomeRule();
            SeasonOutcomeDecision promoted = Decide(rule, 1, 2);
            Assert.AreEqual(SeasonOutcome.Promoted, promoted.Outcome);
            Assert.AreEqual(2, promoted.NextTierIndex);

            SeasonOutcomeDecision demoted = Decide(rule, 1, 27);
            Assert.AreEqual(SeasonOutcome.Demoted, demoted.Outcome);
            Assert.AreEqual(0, demoted.NextTierIndex);

            SeasonOutcomeDecision stayed = Decide(rule, 1, 12);
            Assert.AreEqual(SeasonOutcome.Unchanged, stayed.Outcome);
            Assert.AreEqual(1, stayed.NextTierIndex);
        }

        [Test]
        public void TopTierFirstPlace_AndBottomTierLastPlace_StayUnchanged()
        {
            var rule = new ZoneSeasonOutcomeRule();
            Assert.AreEqual(SeasonOutcome.Unchanged, Decide(rule, 4, 0).Outcome);
            Assert.AreEqual(SeasonOutcome.Unchanged, Decide(rule, 0, 29).Outcome);
        }

        [Test]
        public void NotParticipated_DefaultKeepsTier_InactiveResetMovesToConfiguredTier()
        {
            Assert.AreEqual(SeasonOutcome.Unchanged, Decide(new ZoneSeasonOutcomeRule(), 3, -1, participated: false).Outcome);

            SeasonOutcomeDecision reset = Decide(new ZoneSeasonOutcomeRule(inactiveResetTierIndex: 1), 3, -1, participated: false);
            Assert.AreEqual(SeasonOutcome.Reset, reset.Outcome);
            Assert.AreEqual(1, reset.NextTierIndex);
        }
    }

    [TestFixture]
    public class WinStreakRuleTests
    {
        private static readonly WinStreakLadder Ladder = LeagueTestFactory.CreateStreakLadder();

        [Test]
        public void Wins_ClimbOneLevelEach_AndStopAtMax()
        {
            var rule = new StandardWinStreakRule();
            WinStreakState state = WinStreakState.Empty;
            for (int win = 1; win <= 8; win++)
            {
                state = rule.Apply(state, WinStreakEvent.LevelWon, Ladder);
                Assert.AreEqual(Math.Min(win, Ladder.MaxLevel), state.Level);
            }
        }

        [Test]
        public void WinsPerLevel_Two_NeedsTwoWins()
        {
            var rule = new StandardWinStreakRule(winsPerLevel: 2);
            WinStreakState state = rule.Apply(WinStreakState.Empty, WinStreakEvent.LevelWon, Ladder);
            Assert.AreEqual(0, state.Level);
            Assert.AreEqual(1, state.WinsTowardNextLevel);
            state = rule.Apply(state, WinStreakEvent.LevelWon, Ladder);
            Assert.AreEqual(1, state.Level);
            Assert.AreEqual(0, state.WinsTowardNextLevel);
        }

        [TestCase(WinStreakEvent.LevelLost)]
        [TestCase(WinStreakEvent.LevelQuit)]
        [TestCase(WinStreakEvent.LevelRetried)]
        public void LosingEvents_ResetStreak(WinStreakEvent streakEvent)
        {
            var rule = new StandardWinStreakRule();
            var state = new WinStreakState(3, 0);
            Assert.IsTrue(rule.WouldReset(state, streakEvent));
            Assert.IsTrue(rule.Apply(state, streakEvent, Ladder).IsEmpty);
        }

        [Test]
        public void Revive_KeepsStreakByDefault_ResetsWhenConfigured()
        {
            var state = new WinStreakState(3, 0);
            var keeping = new StandardWinStreakRule();
            Assert.IsFalse(keeping.WouldReset(state, WinStreakEvent.LevelRevived));
            Assert.AreEqual(state, keeping.Apply(state, WinStreakEvent.LevelRevived, Ladder));

            var strict = new StandardWinStreakRule(reviveKeepsStreak: false);
            Assert.IsTrue(strict.WouldReset(state, WinStreakEvent.LevelRevived));
            Assert.IsTrue(strict.Apply(state, WinStreakEvent.LevelRevived, Ladder).IsEmpty);
        }

        [Test]
        public void QuitKeepsStreak_DefaultsOff_QuitStillResets()
        {
            var state = new WinStreakState(3, 0);
            Assert.IsFalse(new StandardWinStreakRule().QuitKeepsStreak);
            Assert.IsFalse(new StandardWinStreakRule(1, true).QuitKeepsStreak);

            var explicitOff = new StandardWinStreakRule(1, true, quitKeepsStreak: false);
            Assert.IsTrue(explicitOff.WouldReset(state, WinStreakEvent.LevelQuit));
            Assert.IsTrue(explicitOff.Apply(state, WinStreakEvent.LevelQuit, Ladder).IsEmpty);
        }

        [Test]
        public void QuitKeepsStreak_On_QuitKeeps_RetryAndLostStillReset()
        {
            var state = new WinStreakState(3, 0);
            var rule = new StandardWinStreakRule(1, true, quitKeepsStreak: true);
            Assert.IsTrue(rule.QuitKeepsStreak);

            Assert.IsFalse(rule.WouldReset(state, WinStreakEvent.LevelQuit));
            Assert.AreEqual(state, rule.Apply(state, WinStreakEvent.LevelQuit, Ladder));

            Assert.IsTrue(rule.WouldReset(state, WinStreakEvent.LevelRetried));
            Assert.IsTrue(rule.Apply(state, WinStreakEvent.LevelRetried, Ladder).IsEmpty);
            Assert.IsTrue(rule.WouldReset(state, WinStreakEvent.LevelLost));
            Assert.IsTrue(rule.Apply(state, WinStreakEvent.LevelLost, Ladder).IsEmpty);
        }

        [Test]
        public void QuitKeepsStreak_IndependentOfReviveKeepsStreak()
        {
            var state = new WinStreakState(3, 0);

            var quitKeepsReviveResets = new StandardWinStreakRule(1, reviveKeepsStreak: false, quitKeepsStreak: true);
            Assert.IsFalse(quitKeepsReviveResets.WouldReset(state, WinStreakEvent.LevelQuit));
            Assert.IsTrue(quitKeepsReviveResets.WouldReset(state, WinStreakEvent.LevelRevived));

            var quitResetsReviveKeeps = new StandardWinStreakRule(1, reviveKeepsStreak: true, quitKeepsStreak: false);
            Assert.IsTrue(quitResetsReviveKeeps.WouldReset(state, WinStreakEvent.LevelQuit));
            Assert.IsFalse(quitResetsReviveKeeps.WouldReset(state, WinStreakEvent.LevelRevived));
        }

        [Test]
        public void QuitKeepsStreak_WinsStillClimb()
        {
            var rule = new StandardWinStreakRule(2, true, true);
            WinStreakState state = rule.Apply(WinStreakState.Empty, WinStreakEvent.LevelWon, Ladder);
            state = rule.Apply(state, WinStreakEvent.LevelQuit, Ladder);
            Assert.AreEqual(1, state.WinsTowardNextLevel, "Thoát giữ cả tiến độ trong bậc");
            state = rule.Apply(state, WinStreakEvent.LevelWon, Ladder);
            Assert.AreEqual(1, state.Level);
        }

        [Test]
        public void WouldReset_FalseWhenNoStreak()
        {
            Assert.IsFalse(new StandardWinStreakRule().WouldReset(WinStreakState.Empty, WinStreakEvent.LevelQuit));
        }

        [Test]
        public void Ladder_LevelZeroMultipliesOne_RewardOnlyInsideLadder()
        {
            var reward = new LeagueRewardPackage(string.Empty, new[] { new LeagueRewardItem("coin", 5) });
            WinStreakLadder ladder = LeagueTestFactory.CreateStreakLadder(reward);
            Assert.AreEqual(1, ladder.MultiplierAt(0));
            Assert.AreEqual(5, ladder.MultiplierAt(99));
            Assert.AreSame(reward, ladder.RewardAt(2));
            Assert.IsTrue(ladder.RewardAt(0).IsEmpty);
            Assert.IsTrue(ladder.RewardAt(6).IsEmpty);
        }
    }

    [TestFixture]
    public class TrophyRuleTests
    {
        private static readonly WinStreakLadder Ladder = LeagueTestFactory.CreateStreakLadder();

        [Test]
        public void BaseByDifficulty_TimesMultiplierOfStreakBeforeWin()
        {
            var rule = new MultipliedTrophyRule(new[] { 5, 8, 12 });
            int trophies = rule.TrophiesForWin(new LevelWinContext(10, 1), new WinStreakState(2, 0), new WinStreakState(3, 0), Ladder);
            Assert.AreEqual(8 * 2, trophies);
        }

        [Test]
        public void DifficultyBeyondTable_UsesLastValue_AfterWinOptionUsesNewStreak()
        {
            var rule = new MultipliedTrophyRule(new[] { 5, 8, 12 }, multiplierFromStreakAfterWin: true);
            int trophies = rule.TrophiesForWin(new LevelWinContext(10, 7), new WinStreakState(2, 0), new WinStreakState(3, 0), Ladder);
            Assert.AreEqual(12 * 3, trophies);
        }
    }

    [TestFixture]
    public class RewardTableTests
    {
        [Test]
        public void FirstMatchingBracketWins_TierSpecificBeforeGeneric()
        {
            var diamondFirst = new LeagueRewardPackage("chest.diamond", new[] { new LeagueRewardItem("coin", 500) });
            var table = new RankBracketRewardTable(new[]
            {
                new LeagueRewardBracket(4, 0, 0, diamondFirst),
            }.Concat(LeagueTestFactory.CreateRewardTable().Brackets));

            Assert.AreSame(diamondFirst, table.RewardFor(4, 0, 30));
            Assert.AreEqual(LeagueTestFactory.GoldChest, table.RewardFor(1, 0, 30).ChestId);
            Assert.AreEqual(LeagueTestFactory.RegularChest, table.RewardFor(1, 29, 30).ChestId);
            Assert.IsTrue(table.RewardFor(1, 30, 30).IsEmpty);
            Assert.IsTrue(table.RewardFor(1, -1, 30).IsEmpty);
        }
    }

    [TestFixture]
    public class SeasonScheduleTests
    {
        [Test]
        public void SeasonsAreContiguous_BoundaryBelongsToNextSeason()
        {
            FixedLengthSeasonSchedule schedule = LeagueTestFactory.CreateSchedule();
            SeasonWindow first = schedule.GetSeasonAt(LeagueTestFactory.Anchor);
            Assert.AreEqual("season-0", first.SeasonId);
            Assert.AreEqual(LeagueTestFactory.Anchor, first.StartUtc);

            SeasonWindow second = schedule.GetSeasonAt(first.EndUtc);
            Assert.AreEqual("season-1", second.SeasonId);
            Assert.AreEqual(first.EndUtc, second.StartUtc);

            SeasonWindow lastTick = schedule.GetSeasonAt(first.EndUtc - TimeSpan.FromTicks(1));
            Assert.AreEqual("season-0", lastTick.SeasonId);
        }

        [Test]
        public void BeforeAnchor_NegativeSeasonNumber()
        {
            FixedLengthSeasonSchedule schedule = LeagueTestFactory.CreateSchedule();
            SeasonWindow before = schedule.GetSeasonAt(LeagueTestFactory.Anchor - TimeSpan.FromTicks(1));
            Assert.AreEqual("season--1", before.SeasonId);
            Assert.AreEqual(LeagueTestFactory.Anchor - LeagueTestFactory.SeasonLength, before.StartUtc);
        }

        [Test]
        public void TimeLeftAndProgress_AreClamped()
        {
            SeasonWindow season = LeagueTestFactory.CreateSchedule().GetSeasonAt(LeagueTestFactory.Anchor);
            Assert.AreEqual(TimeSpan.Zero, season.TimeLeft(season.EndUtc + TimeSpan.FromDays(3)));
            Assert.AreEqual(1.0, season.Progress(season.EndUtc + TimeSpan.FromDays(3)));
            Assert.AreEqual(0.0, season.Progress(season.StartUtc - TimeSpan.FromDays(3)));
            Assert.AreEqual(0.5, season.Progress(season.StartUtc + TimeSpan.FromDays(3.5)), 1e-9);
        }
    }

    [TestFixture]
    public class LeagueTextRecordTests
    {
        [Test]
        public void RoundTrip_EscapesSeparatorsNewlinesAndBackslashes()
        {
            var record = new LeagueTextRecord(3);
            record.SetString("name", "a=b\\c\nd\re Nguyễn");
            record.SetLong("big", long.MaxValue);
            record.SetBool("flag", true);

            Assert.IsTrue(LeagueTextRecord.TryDecode(record.Encode(), out LeagueTextRecord decoded));
            Assert.AreEqual(3, decoded.Format);
            Assert.AreEqual("a=b\\c\nd\re Nguyễn", decoded.GetString("name", null));
            Assert.AreEqual(long.MaxValue, decoded.GetLong("big", 0));
            Assert.IsTrue(decoded.GetBool("flag", false));
        }

        [TestCase("")]
        [TestCase("garbage without separator")]
        [TestCase("format=1\nbad=trailing\\")]
        [TestCase("name=value")]
        public void Decode_RejectsCorruptText(string text)
        {
            Assert.IsFalse(LeagueTextRecord.TryDecode(text, out _));
        }

        [Test]
        public void PackageAndResult_RoundTrip()
        {
            var package = new LeagueRewardPackage("chest.silver", new[] { new LeagueRewardItem("coin", 40), new LeagueRewardItem("booster.wiper", 1) });
            var result = new SeasonResult("season-7", 1, 2, SeasonOutcome.Promoted, 3, 30, 412, package, acknowledged: true, rewardClaimed: false);
            var record = new LeagueTextRecord(1);
            record.SetResult("r", result);

            Assert.IsTrue(LeagueTextRecord.TryDecode(record.Encode(), out LeagueTextRecord decoded));
            SeasonResult restored = decoded.GetResult("r");
            Assert.AreEqual(result.ToString(), restored.ToString());
            Assert.AreEqual("chest.silver", restored.Reward.ChestId);
            Assert.AreEqual(2, restored.Reward.Items.Count);
            Assert.AreEqual("booster.wiper", restored.Reward.Items[1].ItemId);
            Assert.IsTrue(restored.Acknowledged);
            Assert.IsFalse(restored.RewardClaimed);
        }

        [Test]
        public void LocalState_CorruptText_DecodesToEmptyState()
        {
            LeagueLocalState state = LeagueLocalState.Decode("format=999\nstreak.level=4\n");
            Assert.IsTrue(state.Streak.IsEmpty);
            Assert.AreEqual(0, state.PendingTrophyGrants.Count);
        }
    }
}
