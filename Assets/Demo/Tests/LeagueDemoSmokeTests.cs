using System;
using System.Collections;
using DreamTech.Leaderboard.League;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DreamTech.Leaderboard.Demo.Tests
{
    /// <summary>
    /// Chạy bàn thử League thật trong PlayMode: dịch vụ mô phỏng có độ trễ, trạng thái lưu qua PlayerPrefs, đồng hồ tua được.
    /// Test EditMode của package gọi thẳng API; ở đây kiểm cùng luồng đó qua đúng những nút người dùng sẽ bấm.
    /// </summary>
    public class LeagueDemoSmokeTests
    {
        private const string SceneName = "LeagueDemo";
        private const float TimeoutSeconds = 20f;

        private LeagueDemo _demo;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            _demo = UnityEngine.Object.FindAnyObjectByType<LeagueDemo>();
            Assert.IsNotNull(_demo, "Scene thiếu LeagueDemo — dựng lại bằng Tools/DreamTech/Leaderboard/Demo/Build League Demo Scene.");

            // PlayerPrefs sống qua các lần chạy: mỗi test bắt đầu từ trạng thái sạch.
            // Chờ một nhịp trước khi xoá: lệnh gọi dở của scene trước bị huỷ khi panel cũ OnDestroy, huỷ xong mới an toàn.
            yield return new WaitForSecondsRealtime(0.3f);
            _demo.ResetEverything();
            yield return WaitUntil(() => _demo.Page != null && !_demo.IsBusy, "trang League tải xong");
        }

        [UnityTest]
        public IEnumerator WinningLevels_AddsTrophiesAndClimbsStreak()
        {
            _demo.WinLevel(0);
            yield return WaitUntil(() => _demo.Page != null && _demo.Page.Group.LocalTrophies >= 10, "cúp được gửi lên nhóm");

            Assert.AreEqual(1, _demo.League.Streak.Level);
            Assert.AreEqual(0, _demo.League.UnsentTrophies, "Gửi xong thì không còn cúp chờ");

            _demo.WinLevel(1);
            yield return WaitUntil(() => _demo.Page.Group.LocalTrophies >= 25, "cúp lần thắng thứ hai");
            Assert.AreEqual(2, _demo.League.Streak.Level);
        }

        [UnityTest]
        public IEnumerator QuittingLosesStreak_ButRevivingKeepsIt()
        {
            _demo.WinLevel(0);
            yield return WaitUntil(() => _demo.League.Streak.Level == 1, "streak lên bậc 1");

            _demo.RaiseStreakEvent(WinStreakEvent.LevelRevived);
            Assert.AreEqual(1, _demo.League.Streak.Level, "Hồi sinh thì giữ streak");

            Assert.IsTrue(_demo.League.WouldLoseStreak(WinStreakEvent.LevelQuit), "Popup thoát phải được cảnh báo");
            _demo.RaiseStreakEvent(WinStreakEvent.LevelQuit);
            Assert.AreEqual(0, _demo.League.Streak.Level);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FirstPlaceAtSeasonEnd_PromotesAndPaysGoldChest()
        {
            _demo.WinLevel(0);
            yield return WaitUntil(() => _demo.Page.Group.LocalTrophies >= 10, "cúp đầu tiên");

            // Giữ #1 tới hết mùa: bot vẫn kiếm cúp tiếp, chỉ đủ #1 "lúc này" thì đến cuối mùa đã trôi xuống giữa bảng.
            _demo.ClimbToRank(1, holdUntilSeasonEnd: true);
            yield return WaitUntil(() => _demo.Page.LocalRowIndex == 0, "leo lên hạng #1");

            _demo.AdvanceToSeasonEnd();
            yield return WaitUntil(() => _demo.PendingResult != null, "kết quả mùa hiện ra");

            SeasonResult result = _demo.PendingResult;
            Assert.AreEqual(SeasonOutcome.Promoted, result.Outcome);
            Assert.AreEqual(0, result.FinalRank);
            Assert.AreEqual("chest.gold", result.Reward.ChestId);

            _demo.ClaimReward();
            yield return WaitUntil(() => _demo.Wallet.ContainsKey("coin"), "quà vào ví");
            Assert.AreEqual(100, _demo.Wallet["coin"]);

            _demo.AcknowledgeResult();
            yield return WaitUntil(() => _demo.PendingResult == null, "hết việc của mùa cũ");

            Assert.AreEqual(1, _demo.Simulation.CurrentTierIndex, "Mùa mới chạy ở bậc trên");
            Assert.AreEqual(0, _demo.Page.Group.LocalTrophies, "Mùa mới bắt đầu từ 0 cúp");
        }

        [UnityTest]
        public IEnumerator NetworkFailure_KeepsTrophiesQueued_UntilNextSend()
        {
            _demo.FailNextCall();
            _demo.WinLevel(0);
            yield return WaitUntil(() => _demo.League.UnsentTrophies == 10, "cúp nằm lại hàng chờ khi mất mạng");

            // Hàng chờ rỗng ngay giữa lúc tải, còn bảng thì tới cuối lần tải mới có số mới — chờ trạng thái yên rồi mới đọc.
            _demo.Reload();
            yield return WaitUntil(() => _demo.League.UnsentTrophies == 0 && !_demo.IsBusy, "lần gọi sau gửi được");
            Assert.AreEqual(10, _demo.Page.Group.LocalTrophies);
        }

        [UnityTest]
        public IEnumerator LockedLeague_IgnoresWins()
        {
            _demo.ToggleFeatureGate();
            Assert.IsFalse(_demo.League.IsUnlocked);

            _demo.WinLevel(2);
            yield return null;

            Assert.AreEqual(0, _demo.League.Streak.Level);
            Assert.AreEqual(0, _demo.League.UnsentTrophies);
            _demo.ToggleFeatureGate();
        }

        private static IEnumerator WaitUntil(Func<bool> condition, string what)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail("Quá " + TimeoutSeconds + "s mà chưa thấy: " + what);
                yield return null;
            }
        }
    }
}
