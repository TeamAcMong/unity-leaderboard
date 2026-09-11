using System;
using UnityEngine;
using UnityEngine.Events;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>Sink phát UnityEvent cho từng nhịp — nối Feel, haptic, analytics... bằng inspector.</summary>
    public sealed class UnityEventLeaderboardFeedback : MonoBehaviour, ILeaderboardFeedbackSink
    {
        [Serializable]
        public sealed class IntegerEvent : UnityEvent<int>
        {
        }

        [SerializeField] private UnityEvent onRevealStarted = new UnityEvent();
        [SerializeField] private UnityEvent onLift = new UnityEvent();
        [Tooltip("Tham số: người thứ mấy vừa bị vượt (1-based).")]
        [SerializeField] private IntegerEvent onPass = new IntegerEvent();
        [Tooltip("Tham số: tier (0 = hạng nhất, 1 = top 3, 2 = thường).")]
        [SerializeField] private IntegerEvent onLand = new IntegerEvent();
        [SerializeField] private UnityEvent onNewEntry = new UnityEvent();
        [SerializeField] private UnityEvent onScoreImproved = new UnityEvent();
        [SerializeField] private IntegerEvent onCelebrate = new IntegerEvent();
        [SerializeField] private UnityEvent onSkipped = new UnityEvent();
        [SerializeField] private UnityEvent onRevealFinished = new UnityEvent();

        public void OnBeat(LeaderboardBeat beat, in LeaderboardBeatContext context)
        {
            switch (beat)
            {
                case LeaderboardBeat.RevealStarted: onRevealStarted.Invoke(); break;
                case LeaderboardBeat.Lift: onLift.Invoke(); break;
                case LeaderboardBeat.Pass: onPass.Invoke(context.PassIndex); break;
                case LeaderboardBeat.Land: onLand.Invoke((int)context.Tier); break;
                case LeaderboardBeat.NewEntry: onNewEntry.Invoke(); break;
                case LeaderboardBeat.ScoreImproved: onScoreImproved.Invoke(); break;
                case LeaderboardBeat.Celebrate: onCelebrate.Invoke((int)context.Tier); break;
                case LeaderboardBeat.Skipped: onSkipped.Invoke(); break;
                case LeaderboardBeat.RevealFinished: onRevealFinished.Invoke(); break;
            }
        }
    }
}
