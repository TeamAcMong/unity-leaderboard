using System.Collections;
using System.Threading.Tasks;
using DreamTech.Leaderboard.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DreamTech.Leaderboard.Demo.Tests
{
    /// <summary>
    /// Chạy scene demo thật trong PlayMode: widget được tick bởi player loop, host chạy animation mở/đóng, Mock có độ trễ.
    /// Test EditMode của package tua widget bằng tay; ở đây kiểm cùng luồng đó trên Update/LateUpdate thật của từng phiên bản Unity.
    /// </summary>
    public class LeaderboardDemoSmokeTests
    {
        private const string SceneName = "LeaderboardDemo";
        private const float TimeoutSeconds = 20f;

        private LeaderboardDemo _demo;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            _demo = Object.FindAnyObjectByType<LeaderboardDemo>();
            Assert.IsNotNull(_demo, "Scene demo thiếu LeaderboardDemo — dựng lại bằng Tools/DreamTech/Leaderboard/Demo/Build Demo Scene.");
        }

        [UnityTest]
        public IEnumerator EveryHost_Climb12_CompletesAtTargetRank()
        {
            for (int hostIndex = 0; hostIndex < _demo.HostCount; hostIndex++)
            {
                _demo.SelectHost(hostIndex);
                _demo.SetRank(121);
                Task<LeaderboardPresentResult> present = _demo.Climb(12);
                yield return WaitFor(present);

                string host = _demo.CurrentHost.DisplayName;
                Assert.AreEqual(PresentOutcome.Completed, present.Result.Outcome, host);
                Assert.AreEqual(RankChangeKind.RankUp, present.Result.Change.Kind, host);
                Assert.AreEqual(108, present.Result.Change.ToRank, host);
                Assert.IsFalse(_demo.Board.HasUnrevealedChange, host);

                _demo.Close();
                yield return WaitSeconds(0.3f);
            }
        }

        [UnityTest]
        public IEnumerator SkipBeforeLanding_ReportsSkipped_AndMarksRevealed()
        {
            _demo.SelectHost(1);
            _demo.SetRank(121);
            _demo.SetSlowMotion(true);
            Task<LeaderboardPresentResult> present = _demo.Climb(12);
            // Chậm 10 lần: intro + nhấc + leo mất hơn 10 giây, nên 2 giây chắc chắn còn trước nhịp hạ cánh.
            yield return WaitSeconds(2f);
            Assert.IsFalse(present.IsCompleted);
            _demo.Skip();
            yield return WaitFor(present);

            Assert.AreEqual(PresentOutcome.Skipped, present.Result.Outcome);
            Assert.AreEqual(108, present.Result.Change.ToRank);
            Assert.IsFalse(_demo.Board.HasUnrevealedChange);
        }

        [UnityTest]
        public IEnumerator CloseBeforeLanding_ReplaysOnNextReveal()
        {
            _demo.SelectHost(0);
            _demo.SetRank(121);
            _demo.SetSlowMotion(true);
            Task<LeaderboardPresentResult> first = _demo.Climb(12);
            yield return WaitSeconds(2f);
            _demo.Close();
            yield return WaitFor(first);
            Assert.AreEqual(PresentOutcome.Cancelled, first.Result.Outcome);
            Assert.IsTrue(_demo.Board.HasUnrevealedChange);

            _demo.SetSlowMotion(false);
            yield return WaitSeconds(0.3f);
            Task<LeaderboardPresentResult> second = _demo.Reveal();
            yield return WaitFor(second);
            Assert.AreEqual(PresentOutcome.Completed, second.Result.Outcome);
            Assert.AreEqual(RankChangeKind.RankUp, second.Result.Change.Kind);
        }

        [UnityTest]
        public IEnumerator FirstPlaceAndNewPlayer_Complete()
        {
            _demo.SelectHost(2);
            _demo.SetRank(10);
            Task<LeaderboardPresentResult> firstPlace = _demo.ReachRank(1);
            yield return WaitFor(firstPlace);
            Assert.AreEqual(PresentOutcome.Completed, firstPlace.Result.Outcome);
            Assert.AreEqual(0, firstPlace.Result.Change.ToRank);

            Task<LeaderboardPresentResult> newPlayer = _demo.NewPlayer();
            yield return WaitFor(newPlayer);
            Assert.AreEqual(PresentOutcome.Completed, newPlayer.Result.Outcome);
            Assert.AreEqual(RankChangeKind.NewEntry, newPlayer.Result.Change.Kind);
        }

        [UnityTest]
        public IEnumerator BackendFailure_ReportsFailed_ThenNextOpenWorks()
        {
            _demo.SelectHost(0);
            _demo.FailNextCall();
            Task<LeaderboardPresentResult> failed = _demo.Open();
            yield return WaitFor(failed);
            Assert.AreEqual(PresentOutcome.Failed, failed.Result.Outcome);

            Task<LeaderboardPresentResult> retried = _demo.Open();
            yield return WaitFor(retried);
            Assert.AreEqual(PresentOutcome.Completed, retried.Result.Outcome);
        }

        private static IEnumerator WaitFor(Task task)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail("Hết " + TimeoutSeconds + " giây mà widget chưa trình bày xong.");
                yield return null;
            }
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
