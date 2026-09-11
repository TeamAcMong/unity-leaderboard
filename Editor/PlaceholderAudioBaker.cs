using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DreamTech.Leaderboard.EditorTools
{
    /// <summary>
    /// Bake âm tổng hợp tạm (tick gỗ, bloop nhấc lên, ding hai nốt, fanfare arpeggio) thành WAV, thay cho việc tạo AudioClip
    /// lúc chạy như bản tham khảo. Cố ý mềm (sine + envelope). Thay bằng SFX thật khi có.
    /// </summary>
    public static class PlaceholderAudioBaker
    {
        private const int SampleRate = 44100;

        public sealed class AudioSet
        {
            public AudioClip Lift;
            public AudioClip Tick;
            public AudioClip Land;
            public AudioClip Fanfare;

            public bool IsComplete => Lift && Tick && Land && Fanfare;
        }

        private const string BakeMenuPath = "Tools/DreamTech/Leaderboard/Bake Placeholder Audio";

        [MenuItem(BakeMenuPath)]
        private static void BakeFromMenu()
        {
            Bake(LeaderboardEditorPaths.AudioFolder);
        }

        [MenuItem(BakeMenuPath, true)]
        private static bool CanBakeFromMenu()
        {
            return LeaderboardEditorPaths.IsWritable(LeaderboardEditorPaths.AudioFolder);
        }

        public static AudioSet Load(string folder)
        {
            return new AudioSet
            {
                Lift = AssetDatabase.LoadAssetAtPath<AudioClip>(folder + "/LeaderboardLift.wav"),
                Tick = AssetDatabase.LoadAssetAtPath<AudioClip>(folder + "/LeaderboardTick.wav"),
                Land = AssetDatabase.LoadAssetAtPath<AudioClip>(folder + "/LeaderboardLand.wav"),
                Fanfare = AssetDatabase.LoadAssetAtPath<AudioClip>(folder + "/LeaderboardFanfare.wav"),
            };
        }

        public static AudioSet Bake(string folder)
        {
            if (!LeaderboardEditorPaths.IsWritable(folder)) throw new InvalidOperationException("Thư mục chỉ đọc: " + folder);
            LeaderboardEditorPaths.EnsureFolder(folder);
            WriteWave(folder + "/LeaderboardTick.wav", Synthesize(0.06f, Tick));
            WriteWave(folder + "/LeaderboardLift.wav", Synthesize(LiftDuration, Lift));
            WriteWave(folder + "/LeaderboardLand.wav", Synthesize(0.9f, Ding));
            WriteWave(folder + "/LeaderboardFanfare.wav", Synthesize(1.2f, Fanfare));
            return Load(folder);
        }

        // ---------------------------------------------------------------- Dạng sóng

        private const float LiftDuration = 0.14f;
        private static readonly float[] FanfareNotes = { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f };

        private static float Tick(float time)
        {
            float envelope = Mathf.Exp(-time * 70f) * Mathf.Clamp01(time * 3000f);
            return envelope * (0.55f * Mathf.Sin(2f * Mathf.PI * 1100f * time) + 0.15f * Mathf.Sin(2f * Mathf.PI * 2200f * time));
        }

        private static float Lift(float time)
        {
            float envelope = Mathf.Sin(Mathf.PI * Mathf.Clamp01(time / LiftDuration));
            float phase = 2f * Mathf.PI * (420f * time + 0.5f * (760f - 420f) / LiftDuration * time * time);
            return 0.35f * envelope * Mathf.Sin(phase);
        }

        private static float Ding(float time)
        {
            return (Bell(time, 783.99f) * 0.5f + Bell(time - 0.07f, 1046.5f) * 0.6f) * 0.45f;
        }

        private static float Fanfare(float time)
        {
            float sample = 0f;
            for (int note = 0; note < FanfareNotes.Length; note++) sample += Bell(time - 0.1f - note * 0.07f, FanfareNotes[note]) * 0.4f;
            return sample * 0.4f;
        }

        private static float Bell(float time, float frequency)
        {
            if (time < 0f) return 0f;
            float envelope = Mathf.Exp(-time * 5f) * Mathf.Clamp01(time * 400f);
            float angular = 2f * Mathf.PI * frequency * time;
            return envelope * (Mathf.Sin(angular) + 0.35f * Mathf.Sin(2.01f * angular) * Mathf.Exp(-time * 8f) +
                               0.12f * Mathf.Sin(3.02f * angular) * Mathf.Exp(-time * 14f));
        }

        private static float[] Synthesize(float seconds, Func<float, float> waveform)
        {
            int sampleCount = Mathf.CeilToInt(seconds * SampleRate);
            var samples = new float[sampleCount];
            for (int index = 0; index < sampleCount; index++) samples[index] = Mathf.Clamp(waveform(index / (float)SampleRate), -1f, 1f);
            return samples;
        }

        /// <summary>WAV PCM 16-bit mono.</summary>
        private static void WriteWave(string path, float[] samples)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                const short channelCount = 1;
                const short bitsPerSample = 16;
                int byteRate = SampleRate * channelCount * bitsPerSample / 8;
                int dataSize = samples.Length * bitsPerSample / 8;

                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataSize);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });
                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write(channelCount);
                writer.Write(SampleRate);
                writer.Write(byteRate);
                writer.Write((short)(channelCount * bitsPerSample / 8));
                writer.Write(bitsPerSample);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataSize);
                for (int index = 0; index < samples.Length; index++) writer.Write((short)Mathf.RoundToInt(samples[index] * short.MaxValue));
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }
    }
}
