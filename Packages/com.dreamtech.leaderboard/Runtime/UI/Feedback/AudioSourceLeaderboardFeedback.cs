using System;
using UnityEngine;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>
    /// Sink âm thanh dùng AudioSource bake sẵn trong prefab (không AddComponent lúc chạy). Tick khi vượt người cao dần.
    /// Game đã có hệ audio riêng thì gỡ component này khỏi prefab variant và inject sink của game qua
    /// <see cref="LeaderboardWidget.SetFeedbackSinks"/>.
    /// </summary>
    public sealed class AudioSourceLeaderboardFeedback : MonoBehaviour, ILeaderboardFeedbackSink
    {
        [Header("Nguồn phát (bake trong prefab)")]
        [SerializeField] private AudioSource oneShotSource;
        [Tooltip("Nguồn riêng cho tick để đổi pitch không ảnh hưởng các âm khác đang phát.")]
        [SerializeField] private AudioSource tickSource;

        [Header("Clip")]
        [SerializeField] private AudioClip liftClip;
        [SerializeField] private AudioClip tickClip;
        [SerializeField] private AudioClip landClip;
        [SerializeField] private AudioClip fanfareClip;

        [Header("Âm lượng / cao độ")]
        [SerializeField, Range(0f, 1f)] private float volume = 0.7f;
        [SerializeField] private float liftGain = 0.6f;
        [SerializeField] private float tickGain = 0.7f;
        [SerializeField] private float fanfareGain = 0.8f;
        [SerializeField] private float tickBasePitch = 1f;
        [SerializeField] private float tickPitchStep = 0.035f;
        [SerializeField] private float tickMaximumPitch = 1.6f;
        [SerializeField] private float spinPitchRange = 0.35f;
        [SerializeField] private float newEntryPitch = 1.06f;

        private Func<float> _volumeProvider;

        /// <summary>Nối với setting âm lượng của game (0..1). Null = luôn 1.</summary>
        public void SetVolumeProvider(Func<float> volumeProvider)
        {
            _volumeProvider = volumeProvider;
        }

        public void OnBeat(LeaderboardBeat beat, in LeaderboardBeatContext context)
        {
            switch (beat)
            {
                case LeaderboardBeat.Lift:
                    Play(oneShotSource, liftClip, 1f, liftGain);
                    break;
                case LeaderboardBeat.Pass:
                    Play(tickSource, tickClip, Mathf.Min(tickMaximumPitch, tickBasePitch + tickPitchStep * context.PassIndex), tickGain);
                    break;
                case LeaderboardBeat.SpinTick:
                    Play(tickSource, tickClip, Mathf.Lerp(tickBasePitch, tickBasePitch + spinPitchRange, context.Progress), tickGain);
                    break;
                case LeaderboardBeat.Land:
                    Play(oneShotSource, landClip, 1f, 1f);
                    break;
                case LeaderboardBeat.NewEntry:
                    Play(oneShotSource, landClip, newEntryPitch, 1f);
                    break;
                case LeaderboardBeat.Celebrate:
                    Play(oneShotSource, fanfareClip, 1f, fanfareGain);
                    break;
            }
        }

        private void Play(AudioSource source, AudioClip clip, float pitch, float gain)
        {
            if (source == null || clip == null) return;
            float scale = _volumeProvider != null ? Mathf.Clamp01(_volumeProvider()) : 1f;
            if (scale <= 0f) return;
            source.pitch = pitch;
            source.PlayOneShot(clip, volume * gain * scale);
        }
    }
}
