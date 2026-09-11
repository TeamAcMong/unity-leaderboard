using System;
using System.Collections.Generic;

namespace DreamTech.Leaderboard.ViewModel
{
    public enum RevealPhase
    {
        NotStarted = 0,
        Intro = 1,
        Lift = 2,
        Spin = 3,
        Climb = 4,
        Land = 5,
        Pop = 6,
        CountScore = 7,
        Bob = 8,
        Finished = 9,
    }

    /// <summary>Những gì timeline cần view làm mà view-model không tự làm được (hiệu ứng hình ảnh, camera).</summary>
    public interface IRevealListener
    {
        void OnBeat(LeaderboardBeat beat, in LeaderboardBeatContext context);

        /// <summary>Khoảnh khắc chạm đích: rải vài ngôi sao lấp lánh quanh row mình.</summary>
        void OnLanded(RankTier tier, RowState localRow);

        /// <summary>Chỉ top 3 / #1: tia sáng, banner, confetti.</summary>
        void OnCelebrate(RankTier tier, RowState localRow);

        /// <summary>Các row phía dưới vừa được gắn lại sau khi quay số.</summary>
        void OnTailRevealed(IReadOnlyList<RowState> tail);

        /// <summary>Skip: camera phải nhảy tới row mình NGAY trong frame này, trước khi list kiểm vùng nhìn thấy.</summary>
        void OnCameraSnapRequested();
    }

    /// <summary>Giới hạn tần suất một nhịp phản hồi theo đồng hồ model.</summary>
    public struct FeedbackThrottle
    {
        private double _lastTime;
        private bool _hasFired;

        public bool TryFire(double now, float minimumInterval)
        {
            if (_hasFired && now - _lastTime < minimumInterval) return false;
            _hasFired = true;
            _lastTime = now;
            return true;
        }
    }

    /// <summary>
    /// Kịch bản một lần trình bày, thuần C# và được tick từ ngoài (không coroutine), nên test tua được từng tick.
    ///
    /// <list type="bullet">
    /// <item>RankUp: Intro → Lift → (Spin nếu nhảy lớn) → Climb → Land.</item>
    /// <item>NewEntry: Intro → Pop (pill NEW).</item>
    /// <item>ScoreImproved: Intro → CountScore (pill BEST) → Bob.</item>
    /// <item>Unchanged / RankDown: Intro → Bob (nhún nhẹ để mắt tìm thấy row mình).</item>
    /// </list>
    ///
    /// <para>Skip: đưa mọi thứ về trạng thái cuối, hoàn tất đếm điểm, xin camera snap, rồi mới diễn nhịp hạ cánh — phần hạ cánh
    /// luôn được diễn trọn. Điểm là một track của timeline nên không bao giờ bị ghi đè sau khi hạ cánh.</para>
    /// </summary>
    public sealed class RevealTimeline
    {
        private const int MaximumPhasesPerTick = 16;

        private readonly BoardModel _model;
        private readonly MotionSettings _settings;
        private readonly IRevealListener _listener;
        private readonly RowState _local;
        private readonly RankTier _tier;

        private RankUpPlan _plan;
        private bool _isPrepared;
        private RevealPhase _phase = RevealPhase.NotStarted;
        private float _phaseElapsed;
        private float _phaseDuration;
        private bool _isSkipRequested;

        private bool _isScoreCounting;
        private long _scoreFrom;
        private long _scoreTo;
        private float _scoreElapsed;

        private int _lastSpinRank;
        private int _crossedCount;
        private float _landFromScale;
        private float _landFromLift;
        private bool _isTailAppended;
        private FeedbackThrottle _passThrottle;
        private FeedbackThrottle _spinThrottle;

        public RevealTimeline(BoardModel model, IRevealListener listener)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _settings = model.Settings;
            _listener = listener ?? NullRevealListener.Instance;
            Change = model.Scene.Change;
            _local = model.LocalRow ?? throw new InvalidOperationException("Không thể diễn khi board không có row người chơi.");
            _tier = model.TierRule.Classify(Change.ToRank);
        }

        public RankChange Change { get; }
        public RankTier Tier => _tier;
        public RevealPhase Phase => _phase;

        /// <summary>Chỉ có khi RankUp.</summary>
        public RankUpPlan Plan => _plan;

        /// <summary>Đã tới nhịp chạm đích (người chơi coi như đã thấy thay đổi) — host dùng để quyết định ghi snapshot khi đóng giữa chừng.</summary>
        public bool HasReachedLanding { get; private set; }

        public bool WasSkipped { get; private set; }
        public bool IsFinished => _phase == RevealPhase.Finished;
        public int CrossedCount => _crossedCount;

        /// <summary>Dựng trạng thái cũ. Gọi TRƯỚC khi list dựng view lần đầu để không lộ vị trí cuối trong một frame.</summary>
        public void Prepare()
        {
            if (_isPrepared) return;
            _isPrepared = true;
            switch (Change.Kind)
            {
                case RankChangeKind.RankUp:
                    _plan = RankUpPlanner.Prepare(_model, Change);
                    break;
                case RankChangeKind.NewEntry:
                    _local.Scale = 0f;
                    _local.SetDisplayScore(0, _model.Clock, false);
                    break;
                case RankChangeKind.ScoreImproved:
                    _local.SetDisplayScore(Change.FromScore, _model.Clock, false);
                    break;
            }
        }

        public void Start()
        {
            if (_phase != RevealPhase.NotStarted) return;
            Prepare();
            Emit(LeaderboardBeat.RevealStarted, 0f);
            EnterPhase(RevealPhase.Intro, _settings.IntroWait);
        }

        public void RequestSkip()
        {
            if (_phase == RevealPhase.NotStarted || _phase == RevealPhase.Finished) return;
            _isSkipRequested = true;
        }

        public void Tick(float deltaTime)
        {
            if (_phase == RevealPhase.NotStarted || _phase == RevealPhase.Finished) return;
            if (deltaTime < 0f || float.IsNaN(deltaTime)) deltaTime = 0f;

            if (_isSkipRequested)
            {
                _isSkipRequested = false;
                ApplySkip();
                if (_phase == RevealPhase.Finished) return;
            }

            AdvanceScoreCount(deltaTime);
            _phaseElapsed += deltaTime;

            // Frame dài có thể đi qua nhiều pha; phần thời gian dư được chuyển sang pha kế.
            for (int guard = 0; guard < MaximumPhasesPerTick; guard++)
            {
                float progress = _phaseDuration <= 0f ? 1f : Easing.Clamp01(_phaseElapsed / _phaseDuration);
                UpdatePhase(progress);
                if (progress < 1f) return;

                float overflow = Math.Max(0f, _phaseElapsed - _phaseDuration);
                CompletePhase();
                if (_phase == RevealPhase.Finished) return;
                _phaseElapsed = overflow;
            }
        }

        /// <summary>Kết thúc ngay về trạng thái cuối, không phát nhịp nào (đóng giữa chừng, lỗi).</summary>
        public void ForceFinish()
        {
            if (_phase == RevealPhase.Finished) return;
            _isSkipRequested = false;
            if (_isPrepared)
            {
                SettleFinalState(emitTailBeat: false);
                _model.FinishAllTweens();
            }
            _phase = RevealPhase.Finished;
        }

        // ---------------------------------------------------------------- Pha

        private void EnterPhase(RevealPhase phase, float duration)
        {
            _phase = phase;
            _phaseElapsed = 0f;
            _phaseDuration = Math.Max(0f, duration);
        }

        private void UpdatePhase(float progress)
        {
            switch (_phase)
            {
                case RevealPhase.Lift:
                {
                    float eased = Easing.OutCubic(progress);
                    _local.Scale = Easing.LerpUnclamped(1f, _settings.LiftScale, eased);
                    _local.Lift = eased;
                    _local.GlowBoost = eased;
                    break;
                }
                case RevealPhase.Spin:
                {
                    int rank = (int)Math.Round(Easing.LerpUnclamped(_plan.SpinFromRank, _plan.SpinToRank, Easing.InOutCubic(progress)));
                    if (rank != _lastSpinRank)
                    {
                        _lastSpinRank = rank;
                        _local.SetDisplayRank(rank, _model.Clock, _settings, _model.TierRule, true);
                        if (_spinThrottle.TryFire(_model.Clock, _settings.MinimumSpinBeatInterval)) Emit(LeaderboardBeat.SpinTick, progress);
                    }
                    break;
                }
                case RevealPhase.Climb:
                {
                    float slot = Easing.LerpUnclamped(_plan.StartSlot, _plan.StartSlot - _plan.AnimatedCount, Easing.InOutCubic(progress));
                    _local.Slot = slot;
                    while (_crossedCount < _plan.AnimatedCount &&
                           RankUpPlanner.ShouldCross(slot, _plan.StartSlot, _crossedCount, _settings.MakeRoomAt))
                    {
                        CrossNext(true);
                    }
                    break;
                }
                case RevealPhase.Land:
                {
                    _local.Scale = Easing.LerpUnclamped(_landFromScale, 1f, Easing.OutBack(progress, _settings.LandOvershoot));
                    float eased = Easing.OutCubic(progress);
                    _local.Lift = Easing.LerpUnclamped(_landFromLift, 0f, eased);
                    _local.GlowBoost = 1f - eased;
                    break;
                }
                case RevealPhase.Pop:
                    _local.Scale = Easing.OutBack(progress, _settings.NewEntryPopOvershoot);
                    break;
                case RevealPhase.Bob:
                {
                    float wave = (float)Math.Sin(progress * Math.PI);
                    _local.Scale = 1f + _settings.BobAmplitude * wave;
                    _local.GlowBoost = wave;
                    break;
                }
            }
        }

        private void CompletePhase()
        {
            switch (_phase)
            {
                case RevealPhase.Intro:
                    BeginAfterIntro();
                    break;
                case RevealPhase.Lift:
                    if (_plan.IsCompressed) BeginSpin();
                    else if (_plan.AnimatedCount > 0) BeginClimb();
                    else BeginLand();
                    break;
                case RevealPhase.Spin:
                    _local.SetDisplayRank(_plan.SpinToRank, _model.Clock, _settings, _model.TierRule, true);
                    if (_plan.AnimatedCount > 0) BeginClimb();
                    else BeginLand();
                    break;
                case RevealPhase.Climb:
                    CrossRemaining(true);
                    BeginLand();
                    break;
                case RevealPhase.Land:
                    _local.Scale = 1f;
                    _local.Lift = 0f;
                    _local.GlowBoost = 0f;
                    AppendTail(true);
                    Finish();
                    break;
                case RevealPhase.Pop:
                    _local.Scale = 1f;
                    if (_tier != RankTier.Standard) Celebrate();
                    Finish();
                    break;
                case RevealPhase.CountScore:
                    CompleteScoreCount();
                    EnterPhase(RevealPhase.Bob, _settings.BobDuration);
                    break;
                case RevealPhase.Bob:
                    Finish();
                    break;
                default:
                    Finish();
                    break;
            }
        }

        private void BeginAfterIntro()
        {
            switch (Change.Kind)
            {
                case RankChangeKind.RankUp:
                    Emit(LeaderboardBeat.Lift, 0f);
                    StartScoreCount(Change.FromScore, Change.ToScore);
                    EnterPhase(RevealPhase.Lift, _settings.LiftDuration);
                    break;
                case RankChangeKind.NewEntry:
                    BeginPop();
                    break;
                case RankChangeKind.ScoreImproved:
                    HasReachedLanding = true;
                    _local.StartPill(PillContent.Best, 0, _tier, _model.Clock, _settings);
                    Emit(LeaderboardBeat.ScoreImproved, 0f);
                    StartScoreCount(Change.FromScore, Change.ToScore);
                    EnterPhase(RevealPhase.CountScore, _settings.ScoreCountDuration);
                    break;
                default:
                    HasReachedLanding = true;
                    EnterPhase(RevealPhase.Bob, _settings.BobDuration);
                    break;
            }
        }

        private void BeginSpin()
        {
            _lastSpinRank = _local.DisplayRank;
            EnterPhase(RevealPhase.Spin, _settings.SpinDuration);
        }

        private void BeginClimb()
        {
            EnterPhase(RevealPhase.Climb, RankUpPlanner.ClimbDuration(_plan.AnimatedCount, _settings));
        }

        private void BeginLand()
        {
            CompleteScoreCount();
            _local.SetDisplayScore(Change.ToScore, _model.Clock, true);
            HasReachedLanding = true;
            _local.Slot = _plan.FinalSlot;
            _local.SetDisplayRank(Change.ToRank, _model.Clock, _settings, _model.TierRule, true);
            _local.FlashNow(_settings.LandFlashAlpha, _settings.FlashDuration);
            _local.StartShine(_model.Clock, _settings);
            _local.StartPill(PillContent.RankUp, _plan.PassedTotal, _tier, _model.Clock, _settings);

            Emit(LeaderboardBeat.Land, 1f);
            _listener.OnLanded(_tier, _local);
            if (_tier != RankTier.Standard) Celebrate();

            _landFromScale = _local.Scale;
            _landFromLift = _local.Lift;
            EnterPhase(RevealPhase.Land, _settings.LandDuration);
        }

        private void BeginPop()
        {
            HasReachedLanding = true;
            Emit(LeaderboardBeat.NewEntry, 0f);
            _local.StartPill(PillContent.New, 0, _tier, _model.Clock, _settings);
            _local.StartShine(_model.Clock, _settings);
            _local.FlashNow(_settings.LandFlashAlpha, _settings.FlashDuration);
            _listener.OnLanded(_tier, _local);
            StartScoreCount(0, Change.ToScore);
            EnterPhase(RevealPhase.Pop, _settings.NewEntryPopDuration);
        }

        private void Celebrate()
        {
            Emit(LeaderboardBeat.Celebrate, 1f);
            _listener.OnCelebrate(_tier, _local);
        }

        private void Finish()
        {
            SettleFinalState(emitTailBeat: true);
            _phase = RevealPhase.Finished;
            Emit(LeaderboardBeat.RevealFinished, 1f);
        }

        private void SettleFinalState(bool emitTailBeat)
        {
            CompleteScoreCount();
            _local.SetDisplayScore(Change.ToScore, _model.Clock, false);
            if (_plan != null)
            {
                CrossRemaining(false);
                AppendTail(emitTailBeat);
            }
            if (_local.DisplayRank != Change.ToRank) _local.SetDisplayRankImmediate(Change.ToRank);
            _local.Scale = 1f;
            _local.Lift = 0f;
            _local.GlowBoost = 0f;
        }

        // ---------------------------------------------------------------- Skip

        private void ApplySkip()
        {
            if (HasReachedLanding)
            {
                // Nhịp hạ cánh luôn diễn trọn; skip lúc này chỉ hoàn tất việc đếm điểm.
                CompleteScoreCount();
                return;
            }

            WasSkipped = true;
            Emit(LeaderboardBeat.Skipped, 0f);

            switch (Change.Kind)
            {
                case RankChangeKind.RankUp:
                    _isScoreCounting = false;
                    _local.SetDisplayScore(Change.ToScore, _model.Clock, false);
                    _local.Scale = _settings.LiftScale;
                    _local.Lift = 1f;
                    _local.GlowBoost = 1f;
                    CrossRemaining(false);
                    _model.FinishAllTweens();
                    _local.SetDisplayRankImmediate(Change.ToRank);
                    _listener.OnCameraSnapRequested();
                    BeginLand();
                    break;
                case RankChangeKind.NewEntry:
                    _model.FinishAllTweens();
                    _listener.OnCameraSnapRequested();
                    BeginPop();
                    break;
                default:
                    _model.FinishAllTweens();
                    _listener.OnCameraSnapRequested();
                    CompletePhase();
                    break;
            }
        }

        // ---------------------------------------------------------------- Vượt người

        private void CrossNext(bool animate)
        {
            RowState passedRow = _plan.Passed[_crossedCount];
            passedRow.TweenSlot(passedRow.Slot + 1f, _settings.PassSlideDuration);
            passedRow.SetDisplayRank(passedRow.Entry.Rank, _model.Clock, _settings, _model.TierRule, animate);
            _local.SetDisplayRank(passedRow.Entry.Rank - 1, _model.Clock, _settings, _model.TierRule, animate);
            _crossedCount++;
            if (animate && _passThrottle.TryFire(_model.Clock, _settings.MinimumPassBeatInterval)) Emit(LeaderboardBeat.Pass, 0f);
        }

        private void CrossRemaining(bool animate)
        {
            if (_plan == null) return;
            _local.Slot = _plan.FinalSlot;
            while (_crossedCount < _plan.AnimatedCount) CrossNext(animate);
        }

        private void AppendTail(bool notifyListener)
        {
            if (_isTailAppended) return;
            _isTailAppended = true;
            if (_plan == null || _plan.Tail.Count == 0) return;
            _model.AppendRows(_plan.Tail, true);
            if (notifyListener) _listener.OnTailRevealed(_plan.Tail);
        }

        // ---------------------------------------------------------------- Đếm điểm

        private void StartScoreCount(long from, long to)
        {
            _isScoreCounting = true;
            _scoreFrom = from;
            _scoreTo = to;
            _scoreElapsed = 0f;
            _local.SetDisplayScore(from, _model.Clock, false);
        }

        private void AdvanceScoreCount(float deltaTime)
        {
            if (!_isScoreCounting) return;
            _scoreElapsed += deltaTime;
            float progress = _settings.ScoreCountDuration <= 0f ? 1f : Easing.Clamp01(_scoreElapsed / _settings.ScoreCountDuration);
            _local.SetDisplayScore(Easing.LerpLong(_scoreFrom, _scoreTo, Easing.OutCubic(progress)), _model.Clock, true);
            if (progress >= 1f) _isScoreCounting = false;
        }

        private void CompleteScoreCount()
        {
            if (!_isScoreCounting) return;
            _isScoreCounting = false;
            _local.SetDisplayScore(_scoreTo, _model.Clock, true);
        }

        private void Emit(LeaderboardBeat beat, float progress)
        {
            var context = new LeaderboardBeatContext(_tier, _crossedCount, _plan != null ? _plan.AnimatedCount : 0, progress,
                                                     Change.FromRank, Change.ToRank);
            _listener.OnBeat(beat, context);
        }

        private sealed class NullRevealListener : IRevealListener
        {
            public static readonly NullRevealListener Instance = new NullRevealListener();

            public void OnBeat(LeaderboardBeat beat, in LeaderboardBeatContext context)
            {
            }

            public void OnLanded(RankTier tier, RowState localRow)
            {
            }

            public void OnCelebrate(RankTier tier, RowState localRow)
            {
            }

            public void OnTailRevealed(IReadOnlyList<RowState> tail)
            {
            }

            public void OnCameraSnapRequested()
            {
            }
        }
    }
}
