using System;
using System.Collections.Generic;
using DreamTech.Leaderboard.ViewModel;
using NUnit.Framework;

namespace DreamTech.Leaderboard.Tests
{
    /// <summary>
    /// Ghi lại TỪNG tick của một màn diễn (slot + hạng hiển thị của mọi row, cỡ / độ nhấc / glow / điểm / flash của row mình,
    /// mọi nhịp và mọi lời gọi listener kèm tick) để so hai lần chạy với nhau, hoặc so với dấu vân tay đã ghi.
    ///
    /// <para>Có hai cách so vì hai câu hỏi khác nhau: "hai cấu hình có cho CÙNG một màn diễn không" (so nguyên giá trị float
    /// — cùng máy, cùng runtime nên phải trùng từng bit) và "màn diễn có còn y như bản đã chốt không" (so dấu vân tay đã làm
    /// tròn 1e-4 — đủ chặt để bắt mọi thay đổi nhìn thấy được, đủ lỏng để không đỏ vì sai khác bit cuối của <c>Math.Pow</c>
    /// giữa các runtime).</para>
    /// </summary>
    internal sealed class RevealTrace : IRevealListener
    {
        private const double Quantum = 10000.0;

        public readonly List<double> Values = new List<double>();
        public readonly List<LeaderboardBeat> Beats = new List<LeaderboardBeat>();
        public readonly List<int> BeatTicks = new List<int>();
        public readonly List<float> LocalSlots = new List<float>();
        public readonly List<RevealPhase> Phases = new List<RevealPhase>();
        public int LandedCount;
        public int CelebrateCount;
        public int TailRevealedCount;
        public int CameraSnapCount;

        /// <summary>Độ loé lớn nhất của row mình qua mọi frame. KHÔNG vào dấu vân tay (không ghi vào <see cref="Values"/>).</summary>
        public float MaximumLocalFlash;

        private int _tick = -1;

        public BoardModel Model { get; private set; }
        public RevealTimeline Timeline { get; private set; }

        /// <summary>Tick đang chạy (-1 = trước tick đầu, tức trong Start()).</summary>
        public int CurrentTick => _tick;

        /// <summary>
        /// Chạy trọn một màn diễn với bước cố định.
        /// </summary>
        /// <param name="beforeTick">Gọi TRƯỚC mỗi tick (kèm số tick) — để test thả cổng, bỏ qua... đúng một thời điểm.</param>
        public static RevealTrace Run(BoardScene scene, MotionSettings settings, float deltaTime, int maximumTicks,
                                      Action<RevealTrace, int> beforeTick = null)
        {
            var trace = new RevealTrace();
            trace.Model = new BoardModel(scene, settings);
            trace.Timeline = new RevealTimeline(trace.Model, trace);
            trace.Timeline.Start();
            trace.RecordFrame();

            for (int tick = 0; tick < maximumTicks && !trace.Timeline.IsFinished; tick++)
            {
                trace._tick = tick;
                beforeTick?.Invoke(trace, tick);
                trace.Timeline.Tick(deltaTime);
                trace.Model.Advance(deltaTime);
                trace.RecordFrame();
            }
            return trace;
        }

        /// <summary>Dấu vân tay FNV-1a 64 bit của mọi giá trị đã ghi, làm tròn 1e-4.</summary>
        public ulong Fingerprint()
        {
            ulong hash = 14695981039346656037UL;
            for (int index = 0; index < Values.Count; index++)
            {
                long quantized = (long)Math.Round(Values[index] * Quantum);
                unchecked
                {
                    for (int shift = 0; shift < 64; shift += 8)
                    {
                        hash ^= (byte)(quantized >> shift);
                        hash *= 1099511628211UL;
                    }
                }
            }
            return hash;
        }

        public int CountOf(LeaderboardBeat beat)
        {
            int count = 0;
            for (int index = 0; index < Beats.Count; index++)
            {
                if (Beats[index] == beat) count++;
            }
            return count;
        }

        public int IndexOfBeat(LeaderboardBeat beat)
        {
            return Beats.IndexOf(beat);
        }

        public int LastIndexOfBeat(LeaderboardBeat beat)
        {
            return Beats.LastIndexOf(beat);
        }

        private void RecordFrame()
        {
            Phases.Add(Timeline.Phase);
            LocalSlots.Add(Model.LocalRow.Slot);
            Values.Add((int)Timeline.Phase);
            Values.Add(Model.Rows.Count);
            IReadOnlyList<RowState> rows = Model.Rows;
            for (int index = 0; index < rows.Count; index++)
            {
                RowState row = rows[index];
                Values.Add(row.Slot);
                Values.Add(row.DisplayRank);
            }

            RowState local = Model.LocalRow;
            Values.Add(local.Scale);
            Values.Add(local.Lift);
            Values.Add(local.GlowBoost);
            Values.Add(local.DisplayScore);
            Values.Add(local.Flash);
            if (local.Flash > MaximumLocalFlash) MaximumLocalFlash = local.Flash;
        }

        public void OnBeat(LeaderboardBeat beat, in LeaderboardBeatContext context)
        {
            Beats.Add(beat);
            BeatTicks.Add(_tick);
            Values.Add(1000 + (int)beat);
            Values.Add(_tick);
            Values.Add(context.PassIndex);
            Values.Add(context.PassTotal);
            Values.Add(context.Progress);
            Values.Add((int)context.Tier);
        }

        public void OnLanded(RankTier tier, RowState localRow)
        {
            LandedCount++;
            Values.Add(2000);
            Values.Add(_tick);
        }

        public void OnCelebrate(RankTier tier, RowState localRow)
        {
            CelebrateCount++;
            Values.Add(3000);
            Values.Add(_tick);
        }

        public void OnTailRevealed(IReadOnlyList<RowState> tail)
        {
            TailRevealedCount++;
            Values.Add(4000);
            Values.Add(_tick);
        }

        public void OnCameraSnapRequested()
        {
            CameraSnapCount++;
            Values.Add(5000);
            Values.Add(_tick);
        }
    }

    /// <summary>
    /// "Cờ tắt thì y như cũ": dấu vân tay từng tick của các màn lên hạng vào / trong top 3, ghi từ code TRƯỚC khi có
    /// <c>MotionSettings.HostPresentedTopRanks</c>.
    ///
    /// <para><b>Vì sao cần dấu vân tay cứng.</b> Cờ mới chen vào đúng những pha mà các màn lên hạng top 3 đi qua (Lift → Climb
    /// → Land). So "cờ tắt" với "cờ tắt" của chính code mới thì không chứng minh được gì; phải so với bản đã chạy thật trong
    /// game. Các con số dưới đây được ghi từ code cũ, với cả cấu hình mặc định lẫn cấu hình kiểu Golden Race (lật hạng sớm, host
    /// đếm điểm, cổng giữ Intro, cú đáp ba đoạn).</para>
    ///
    /// <para><b>Đỏ ở đây mà bạn CỐ Ý đổi mặc định / planner:</b> chạy lại, lấy số mới trong thông báo lỗi và ghi đè — nhưng chỉ
    /// sau khi đã chắc là đổi hành vi của mọi game đang chạy là điều bạn muốn.</para>
    /// </summary>
    [TestFixture]
    public class RevealTimelineFlagOffTests
    {
        private const float FrameDeltaTime = 1f / 60f;
        private const int MaximumTicks = 4000;

        /// <summary>Thả cổng Intro ở tick này trong cấu hình kiểu host (giữa lúc đang chờ).</summary>
        private const int HostReleaseTick = 40;

        internal static MotionSettings HostLikeSettings()
        {
            return new MotionSettings
            {
                LiftDuration = 0.2f,
                LiftScale = 1.06f,
                ClimbSecondsPerRow = 0.084f,
                ClimbDurationMinimum = 0.5f,
                ClimbDurationMaximum = 1f,
                SpinDuration = 0.08f,
                PassSlideDuration = 0.18f,
                ClimbEasePower = 2f,
                LandDuration = 0.26f,
                LandPeakScale = 1.134f,
                LandTroughScale = 0.958f,
                LandFlashAt = 0.51f,
                FlashRiseDuration = 0.12f,
                FlashDecayPower = 1.4f,
                LandTwinklesAt = 0.35f,
                RankUpPill = false,
                RankUpShine = false,
                LandGlowFadesLate = true,
                LandOvershoot = 0f,
                LandFlashAlpha = 0.22f,
                FlashDuration = 0.45f,
                HostOwnsScoreCount = true,
                RankFlipsBeforeRankMove = true,
                WaitForHostRelease = true,
                HostHoldTimeout = 8f,
                RankRollDuration = 0.06f,
            };
        }

        internal static RevealTrace RunScenario(int startRank, int targetRank, MotionSettings settings, int skipAtTick)
        {
            BoardScene scene = LeaderboardScenario.Create(maximumAnimatedPasses: 15).LoadRevealScene(startRank, targetRank);
            return RevealTrace.Run(scene, settings, FrameDeltaTime, MaximumTicks, (trace, tick) =>
            {
                if (tick == HostReleaseTick) trace.Timeline.ReleaseHostHold();
                if (tick == skipAtTick) trace.Timeline.RequestSkip();
            });
        }

        private static IEnumerable<TestCaseData> Goldens()
        {
            // (hạng cũ 0-based, hạng mới 0-based, cấu hình kiểu host?, tick bỏ qua, dấu vân tay đã ghi từ code cũ)
            yield return new TestCaseData(9, 1, false, -1, 16924963378058266724UL).SetName("FlagOff_Default_10To2_MatchesRecordedTrajectory");
            yield return new TestCaseData(2, 1, false, -1, 10055797647148818254UL).SetName("FlagOff_Default_3To2_MatchesRecordedTrajectory");
            yield return new TestCaseData(5, 0, false, -1, 13600279823079044113UL).SetName("FlagOff_Default_6To1_MatchesRecordedTrajectory");
            yield return new TestCaseData(47, 36, false, -1, 3560637390046749368UL).SetName("FlagOff_Default_48To37_MatchesRecordedTrajectory");
            yield return new TestCaseData(9, 1, false, 30, 3808460288999170637UL).SetName("FlagOff_Default_10To2_SkipMidClimb_MatchesRecordedTrajectory");
            yield return new TestCaseData(9, 1, true, -1, 7621990182344231237UL).SetName("FlagOff_HostLike_10To2_MatchesRecordedTrajectory");
            yield return new TestCaseData(2, 1, true, -1, 9529611556652627674UL).SetName("FlagOff_HostLike_3To2_MatchesRecordedTrajectory");
            yield return new TestCaseData(5, 0, true, -1, 17298283785611670032UL).SetName("FlagOff_HostLike_6To1_MatchesRecordedTrajectory");
            yield return new TestCaseData(47, 36, true, -1, 15465992187151253845UL).SetName("FlagOff_HostLike_48To37_MatchesRecordedTrajectory");
        }

        [TestCaseSource(nameof(Goldens))]
        public void FlagOff_MatchesRecordedTrajectory(int startRank, int targetRank, bool hostLike, int skipAtTick, ulong expected)
        {
            MotionSettings settings = hostLike ? HostLikeSettings() : new MotionSettings();
            RevealTrace trace = RunScenario(startRank, targetRank, settings, skipAtTick);

            Assert.IsTrue(trace.Timeline.IsFinished, "Màn diễn phải kết thúc.");
            Assert.AreEqual(expected, trace.Fingerprint(),
                            "Quỹ đạo / nhịp của màn diễn khi cờ TẮT đã khác bản đã ghi (" + trace.Values.Count +
                            " giá trị, " + trace.Beats.Count + " nhịp). Dấu vân tay mới: " + trace.Fingerprint() + "UL");
        }
    }
}
