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

        /// <summary>
        /// Row mình đứng ở ranh giới của phần host trình bày (<c>MotionSettings.HostPresentedTopRanks</c>) chờ host diễn cú
        /// lên bục rồi gọi <see cref="RevealTimeline.ReleasePodiumHold"/> (hoặc hết <c>HostHoldTimeout</c>).
        /// </summary>
        PodiumHold = 10,

        /// <summary>Sau khi host thả cổng bục: row mình đi nốt từ ranh giới tới ô đích (<c>PodiumClimbDuration</c>), rồi Land.</summary>
        PodiumClimb = 11,
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
    /// <item>RankUp đáp vào phần host trình bày (<c>MotionSettings.HostPresentedTopRanks</c>): Intro → Lift → (Spin) →
    /// (Climb tới ranh giới, nếu bắt đầu dưới ranh giới) → PodiumHold (nhịp PodiumTakeover, chờ host) → PodiumClimb → Land.
    /// Có <c>PodiumApproachScrollSpeed</c> thì Climb là cú tiếp cận kiểu cuộn và LUÔN có (kể cả bắt đầu ở ranh giới); có
    /// <c>DeferPodiumApproachPasses</c> thì người bị vượt trong đoạn đó đứng yên tới tick host thả cổng.</item>
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

        /// <summary>Số ô đầu do host trình bày (<see cref="BoardModel.HiddenLeadingSlots"/>) — cũng là ô ranh giới.</summary>
        private readonly int _hiddenLeadingSlots;

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

        /// <summary>
        /// Số người bị vượt trong đoạn leo TRONG LIST. Bằng <c>AnimatedCount</c> khi không lên bục (mọi công thức y như cũ);
        /// khi lên bục thì chỉ tới ranh giới — những người phía trên ranh giới được vượt ở pha PodiumClimb.
        /// </summary>
        private int _listPassCount;

        /// <summary>Host đã thả cổng bục chưa. Một chiều, như <see cref="_hostReleased"/>.</summary>
        private bool _podiumReleased;

        /// <summary>Đã đứng ở cổng bục bao nhiêu giây — để bỏ cuộc theo <c>HostHoldTimeout</c>.</summary>
        private float _podiumHoldElapsed;

        /// <summary>Slot row mình lúc bắt đầu pha PodiumClimb (chỗ nó đứng chờ).</summary>
        private float _podiumClimbFromSlot;

        /// <summary>
        /// Đoạn trong list của màn lên bục này là cú TIẾP CẬN kiểu cuộn (<c>MotionSettings.PodiumApproachScrollSpeed</c> &gt; 0,
        /// bắt đầu ở một ô ≥ ranh giới). Chốt một lần ở <see cref="Prepare"/> và ghi sang model (<c>HasPodiumApproach</c>) cho list.
        /// </summary>
        private bool _hasPodiumApproach;

        /// <summary>Host đã cho phép đi tiếp chưa (chỉ có nghĩa khi <c>WaitForHostRelease</c> bật).</summary>
        private bool _hostReleased;

        /// <summary>
        /// Pha Intro hiện tại là một nhịp CHỜ HOST tự gia hạn (đã hết <c>IntroWait</c>), không phải đoạn Intro thật. Chỉ nhịp chờ
        /// mới được lời thả cổng cắt ngang (xem <see cref="IsWaitingOnReleasedGate"/>): thả sớm trong lúc Intro thật còn chạy thì
        /// Intro vẫn phải chạy đủ, đúng hợp đồng "thả trước khi tới cũng được".
        /// </summary>
        private bool _isPollingHostHold;

        /// <summary>Flash trong pha đáp đã nổ chưa (chỉ dùng khi <c>LandFlashAt</c> ≥ 0).</summary>
        private bool _landFlashPlayed;

        /// <summary>Sao trong pha đáp đã bung chưa (chỉ dùng khi <c>LandTwinklesAt</c> ≥ 0).</summary>
        private bool _landTwinklesPlayed;

        /// <summary>
        /// Cú đáp ba đoạn (xem <c>MotionSettings.LandPeakScale</c>): vọt lên đỉnh → hụt xuống đáy → về 1.
        ///
        /// <para>Số mặc định lấy từ clip tham chiếu, đo bề ngang + chiều cao hàng từng khung: 1,06 → 1,134 (0,08 s) →
        /// 0,958 (0,205 s) → 1,0 (0,26 s). OutQuad lên / InQuad xuống / OutQuad về khớp các điểm đo trong ±2 %.</para>
        /// </summary>
        private float LandPunchScale(float progress)
        {
            float peakAt = Easing.Clamp(_settings.LandPeakAt, 0.01f, 0.98f);
            float troughAt = Easing.Clamp(_settings.LandTroughAt, peakAt + 0.01f, 0.99f);
            float peak = _settings.LandPeakScale;
            float trough = _settings.LandTroughScale;

            if (progress < peakAt)
                return Easing.LerpUnclamped(_landFromScale, peak, Easing.OutQuad(progress / peakAt));
            if (progress < troughAt)
            {
                float fall = (progress - peakAt) / (troughAt - peakAt);
                return Easing.LerpUnclamped(peak, trough, fall * fall);
            }
            return Easing.LerpUnclamped(trough, 1f, Easing.OutQuad((progress - troughAt) / (1f - troughAt)));
        }

        /// <summary>
        /// Nhịp "đáp" (pill ▲N, sao vàng, flash, shine) đã diễn rồi chưa.
        ///
        /// <para>Là một CỜ chứ không suy ra từ <c>RankFlipsBeforeRankMove</c>: có một đường mà cờ cấu hình bật
        /// nhưng nhịp sớm CHƯA chạy — bỏ qua giữa lúc host đang giữ cổng (trước cả BeginAfterIntro). Suy từ cấu
        /// hình thì <see cref="BeginLand"/> sẽ tưởng đã diễn rồi và nuốt mất pill lẫn sao trên đường bỏ qua.</para>
        /// </summary>
        private bool _landingFeedbackPlayed;

        /// <summary>Đã chờ host bao nhiêu giây — để còn bỏ cuộc theo <c>HostHoldTimeout</c>.</summary>
        private float _hostHoldElapsed;

        /// <summary>
        /// Đã hết giờ chờ host mà vẫn chưa được thả cổng.
        ///
        /// <para>Chỉ ĐẶT CỜ chứ không tự ghi log: assembly này là C# thuần (<c>noEngineReferences: true</c>)
        /// nên không có <c>UnityEngine.Debug</c>. Lớp UI đọc cờ và kêu hộ — đúng chỗ đã có sẵn Unity.</para>
        /// </summary>
        public bool HostHoldTimedOut { get; private set; }

        /// <summary>
        /// Đã hết <c>HostHoldTimeout</c> ở cổng bục mà host vẫn chưa gọi <see cref="ReleasePodiumHold"/>. Như
        /// <see cref="HostHoldTimedOut"/>: chỉ đặt cờ, lớp UI kêu hộ.
        /// </summary>
        public bool PodiumHoldTimedOut { get; private set; }
        private FeedbackThrottle _passThrottle;
        private FeedbackThrottle _spinThrottle;

        /// <summary>
        /// Người đã bị vượt nhưng còn ĐỨNG YÊN chờ cú đáp (<c>MotionSettings.DeferPassSlidesToLand</c>). Thứ tự = thứ tự bị vượt.
        /// </summary>
        private readonly List<RowState> _deferredPasses = new List<RowState>();

        /// <summary>Các row trong <see cref="_deferredPasses"/> đã bắt đầu dời xuống (lúc đáp) chưa.</summary>
        private bool _deferredSlidesStarted;

        /// <summary>Số nhịp tick đã phát trong cú leo hiện tại (<c>MotionSettings.ClimbTickInterval</c>).</summary>
        private int _climbTicksEmitted;

        /// <summary>
        /// <see cref="MotionSettings.CoroutineFrameTiming"/>: pha hiện tại đã vẽ mẫu cuối ở tick trước, tick này mới trao cho pha kế.
        /// </summary>
        private bool _completesOnNextTick;

        /// <summary><see cref="MotionSettings.CoroutineFrameTiming"/>: pha hiện tại chưa được tính khung nào.</summary>
        private bool _isPhaseFresh;

        /// <summary>
        /// <see cref="MotionSettings.CoroutineFrameTiming"/>: độ dài khung đầu của pha hiện tại — đồng hồ tick đếm từ KHUNG đầu (không
        /// gồm độ dài của nó), còn mẫu vẽ thì gồm.
        /// </summary>
        private float _phaseFirstFrameDelta;

        /// <summary><see cref="MotionSettings.CoroutineFrameTiming"/>: đồng hồ tick (xem trên) lúc tick gần nhất nổ.</summary>
        private float _lastClimbTickClock;

        public RevealTimeline(BoardModel model, IRevealListener listener)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _settings = model.Settings;
            _listener = listener ?? NullRevealListener.Instance;
            Change = model.Scene.Change;
            _local = model.LocalRow ?? throw new InvalidOperationException("Không thể diễn khi board không có row người chơi.");
            _tier = model.TierRule.Classify(Change.ToRank);
            _hiddenLeadingSlots = model.HiddenLeadingSlots;
        }

        public RankChange Change { get; }
        public RankTier Tier => _tier;
        public RevealPhase Phase => _phase;

        /// <summary>Chỉ có khi RankUp.</summary>
        public RankUpPlan Plan => _plan;

        /// <summary>
        /// Màn lên hạng này đáp vào phần host trình bày (cờ <c>HostPresentedTopRanks</c> bật, RankUp, ô đích &lt;
        /// <see cref="BoardModel.HiddenLeadingSlots"/>): leo trong list tới ranh giới, dừng ở PodiumHold chờ host, rồi mới đi
        /// nốt. Có giá trị sau <see cref="Prepare"/>. NewEntry / RankDown / Unchanged không bao giờ lên bục kiểu này — host
        /// dựng thẳng trạng thái của chúng.
        /// </summary>
        public bool TakesPodium { get; private set; }

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
                    _listPassCount = _plan.AnimatedCount;
                    TakesPodium = _hiddenLeadingSlots > 0 && _plan.FinalSlot < _hiddenLeadingSlots;
                    if (TakesPodium)
                    {
                        // Ranh giới là ô Hidden; đoạn trong list chỉ vượt những người đang đứng ở ô ≥ ranh giới. Bắt đầu ở
                        // ngay ranh giới (4 → 3) hoặc đã trên bục (3 → 2) thì đoạn này rỗng: nhấc lên xong là tới cổng.
                        _listPassCount = Math.Max(0, _plan.LocalIndex + _plan.AnimatedCount - _hiddenLeadingSlots);

                        // Cú tiếp cận kiểu cuộn chỉ có khi row mình bắt đầu TRONG list (kể cả đúng ô ranh giới). Đổi chỗ trên bục
                        // (3 → 2) không có đoạn list nào để cuộn: host diễn cú đó từ ngay nhịp Lift.
                        if (_settings.PodiumApproachScrollSpeed > 0f && _plan.StartSlot >= _hiddenLeadingSlots)
                        {
                            _hasPodiumApproach = true;
                            _model.BeginPodiumApproach();
                        }
                    }
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
            if (_settings.CoroutineFrameTiming)
            {
                TickWithCoroutineFrameTiming(deltaTime);
                return;
            }
            _phaseElapsed += deltaTime;

            // Frame dài có thể đi qua nhiều pha; phần thời gian dư được chuyển sang pha kế.
            for (int guard = 0; guard < MaximumPhasesPerTick; guard++)
            {
                // Nhịp chờ host mà cổng đã thả thì xong NGAY ở tick này, dù nhịp 1/60 s đó mới trôi được một nửa: frame ngắn
                // hơn nhịp chờ (120 Hz, hay 60 Hz mà dt dao động) sẽ để lời thả trễ thêm một frame tuỳ pha của nhịp — đúng
                // frame host đã huỷ proxy của nó vì tin model đổi ngay. Phần dư = 0 khi cắt ngang (chưa hết nhịp); đủ nhịp thì
                // vẫn chuyển phần dư như cũ, nên ở bước 1/60 s quỹ đạo không lệch một bit.
                float progress = _phaseDuration <= 0f || IsWaitingOnReleasedGate
                    ? 1f
                    : Easing.Clamp01(_phaseElapsed / _phaseDuration);
                UpdatePhase(progress);
                if (progress < 1f) return;

                float overflow = Math.Max(0f, _phaseElapsed - _phaseDuration);
                CompletePhase();
                if (_phase == RevealPhase.Finished) return;
                _phaseElapsed = overflow;
            }
        }

        /// <summary>
        /// Tick theo nhịp khung của coroutine (<see cref="MotionSettings.CoroutineFrameTiming"/>). Mỗi pha như một vòng
        /// <c>elapsed += dt; vẽ; yield</c>: khung đầu của pha đã được tính độ dài của chính nó; hết giờ thì vẽ mẫu cuối và CHỈ
        /// trao cho pha kế ở tick sau (vòng của họ kiểm tra điều kiện sau <c>yield</c>). Pha dài 0 giây và nhịp chờ host đã được
        /// thả thì trao ngay trong tick — pha kế nhận lại trọn độ dài khung này, như một coroutine được gọi thẳng từ callback.
        /// </summary>
        private void TickWithCoroutineFrameTiming(float deltaTime)
        {
            bool isFrameCredited = false;
            for (int guard = 0; guard < MaximumPhasesPerTick; guard++)
            {
                if (_completesOnNextTick)
                {
                    _completesOnNextTick = false;
                    CompletePhase();
                    if (_phase == RevealPhase.Finished) return;
                }
                if (!isFrameCredited)
                {
                    if (_isPhaseFresh)
                    {
                        _isPhaseFresh = false;
                        _phaseFirstFrameDelta = deltaTime;
                    }
                    _phaseElapsed += deltaTime;
                    isFrameCredited = true;
                }

                bool handsOverNow = _phaseDuration <= 0f || IsWaitingOnReleasedGate;
                float progress = handsOverNow ? 1f : Easing.Clamp01(_phaseElapsed / _phaseDuration);
                UpdatePhase(progress);
                if (progress < 1f) return;
                if (!handsOverNow)
                {
                    _completesOnNextTick = true;
                    return;
                }
                CompletePhase();
                if (_phase == RevealPhase.Finished) return;
                isFrameCredited = false;
            }
        }

        /// <summary>Kết thúc ngay về trạng thái cuối, không phát nhịp nào (đóng giữa chừng, lỗi).</summary>
        public void ForceFinish()
        {
            if (_phase == RevealPhase.Finished) return;
            _isSkipRequested = false;
            // Đóng giữa lúc đứng ở cổng bục = coi như host đã thả: trạng thái cuối được dựng thẳng, không còn gì để chờ.
            _podiumReleased = true;
            if (_isPrepared)
            {
                SettleFinalState(emitTailBeat: false, keepGlowEnvelope: false);
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
            _completesOnNextTick = false;
            _isPhaseFresh = true;
            _phaseFirstFrameDelta = 0f;
            _lastClimbTickClock = 0f;
        }

        private void UpdatePhase(float progress)
        {
            switch (_phase)
            {
                case RevealPhase.Lift:
                {
                    float eased = _settings.LiftCurve != null ? _settings.LiftCurve.Evaluate(progress) : Easing.OutCubic(progress);
                    _local.Scale = Easing.LerpUnclamped(1f, _settings.LiftScale, eased);
                    _local.Lift = eased;
                    if (!_local.IsGlowEnvelopeActive) _local.GlowBoost = eased;
                    break;
                }
                case RevealPhase.Spin:
                {
                    // Đã lật số hạng từ đầu ⇒ không còn gì để quay. Quay tiếp là kéo con số từ hạng MỚI lùi về
                    // SpinFromRank rồi lăn lại — đúng cái lỗi "lật xong rồi lăn ngược" của CrossNext bên dưới.
                    if (_settings.RankFlipsBeforeRankMove) break;

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
                    float eased = ClimbEased(progress);
                    float slot = Easing.LerpUnclamped(_plan.StartSlot, ListClimbEndSlot, eased);
                    _local.Slot = slot;
                    // Camera của cú tiếp cận đọc CHÍNH tiến độ này (cùng tick, cùng đường cong) — xem MotionSettings.PodiumApproachScrollSpeed.
                    if (_hasPodiumApproach) _model.SetPodiumApproachProgress(eased);
                    while (_crossedCount < _listPassCount &&
                           RankUpPlanner.ShouldCross(slot, _plan.StartSlot, _crossedCount, _settings.MakeRoomAt))
                    {
                        CrossNext(true);
                    }
                    if (_settings.ClimbTickInterval > 0f) EmitDueClimbTick();
                    break;
                }
                case RevealPhase.PodiumClimb:
                {
                    // Cùng đường cong và cùng luật vượt với đoạn trong list, chỉ khác điểm xuất phát (chỗ đứng chờ ở cổng)
                    // và thời lượng — luật vượt tính từ StartSlot nên nối tiếp liền mạch số người đã vượt ở đoạn trước.
                    float slot = Easing.LerpUnclamped(_podiumClimbFromSlot, _plan.FinalSlot, ClimbEased(progress));
                    _local.Slot = slot;
                    while (_crossedCount < _plan.AnimatedCount &&
                           RankUpPlanner.ShouldCross(slot, _plan.StartSlot, _crossedCount, _settings.MakeRoomAt))
                    {
                        CrossNext(IsPodiumClimbAnimated);
                    }
                    break;
                }
                case RevealPhase.Land:
                {
                    _local.Scale = _settings.LandCurve != null
                        ? Easing.LerpUnclamped(_landFromScale, 1f, _settings.LandCurve.Evaluate(progress))
                        : _settings.LandPeakScale > 0f
                            ? LandPunchScale(progress)
                            : Easing.LerpUnclamped(_landFromScale, 1f, Easing.OutBack(progress, _settings.LandOvershoot));
                    if (_settings.LandFlashAt >= 0f && !_landFlashPlayed && progress >= _settings.LandFlashAt)
                    {
                        _landFlashPlayed = true;
                        _local.FlashNow(_settings.LandFlashAlpha, _settings.FlashDuration, _settings.FlashRiseDuration,
                                        _settings.FlashDecayPower);
                    }
                    if (_settings.LandTwinklesAt >= 0f && !_landTwinklesPlayed && progress >= _settings.LandTwinklesAt)
                    {
                        _landTwinklesPlayed = true;
                        _listener.OnLanded(_tier, _local);
                    }
                    float eased = Easing.OutCubic(progress);
                    _local.Lift = Easing.LerpUnclamped(_landFromLift, 0f, eased);
                    if (!_local.IsGlowEnvelopeActive)
                    {
                        _local.GlowBoost = _settings.LandGlowFadesLate ? 1f - progress * progress * progress : 1f - eased;
                    }
                    break;
                }
                case RevealPhase.Pop:
                    _local.Scale = Easing.OutBack(progress, _settings.NewEntryPopOvershoot);
                    break;
                case RevealPhase.Bob:
                {
                    if (_settings.QuietPulseInsteadOfBob)
                    {
                        _local.Scale = QuietPulseScale(progress * _phaseDuration);
                        break;
                    }
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
                    if (ShouldKeepHoldingForHost())
                    {
                        // Tự gia hạn pha Intro thành từng nhịp ngắn thay vì một lần chờ dài; nhịp chờ nào cũng bị
                        // ReleaseHostHold() cắt ngang ở tick kế tiếp (xem IsWaitingOnReleasedGate).
                        _hostHoldElapsed += _phaseDuration;
                        _isPollingHostHold = true;
                        EnterPhase(RevealPhase.Intro, HostHoldPollSeconds);
                        break;
                    }
                    BeginAfterIntro();
                    break;
                case RevealPhase.Lift:
                    if (_plan.IsCompressed) BeginSpin();
                    else BeginClimbOrHold();
                    break;
                case RevealPhase.Spin:
                    if (!_settings.RankFlipsBeforeRankMove)
                    {
                        _local.SetDisplayRank(_plan.SpinToRank, _model.Clock, _settings, _model.TierRule, true);
                    }
                    BeginClimbOrHold();
                    break;
                case RevealPhase.Climb:
                    if (TakesPodium)
                    {
                        CrossUpTo(_listPassCount, true);
                        EndPodiumApproach();
                        BeginPodiumHold();
                        break;
                    }
                    CrossRemaining(true);
                    BeginLand();
                    break;
                case RevealPhase.PodiumHold:
                    _podiumHoldElapsed += _phaseDuration;
                    if (ShouldKeepHoldingForPodium())
                    {
                        // Tự gia hạn từng nhịp ngắn như cổng Intro; ReleasePodiumHold() cắt nhịp đang chờ ở tick kế tiếp.
                        EnterPhase(RevealPhase.PodiumHold, HostHoldPollSeconds);
                        break;
                    }
                    BeginPodiumClimb();
                    break;
                case RevealPhase.PodiumClimb:
                    CrossRemaining(IsPodiumClimbAnimated);
                    BeginLand();
                    break;
                case RevealPhase.Land:
                    _local.Scale = 1f;
                    _local.Lift = 0f;
                    if (!_local.IsGlowEnvelopeActive) _local.GlowBoost = 0f;
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
                    EnterPhase(RevealPhase.Bob, SkipsHostPresentedPulse ? 0f : BobPhaseDuration);
                    break;
                case RevealPhase.Bob:
                    Finish();
                    break;
                default:
                    Finish();
                    break;
            }
        }

        /// <summary>
        /// Nhịp tự gia hạn khi đang chờ host (bộ đếm hết giờ cộng theo nhịp này). Lời thả cổng KHÔNG phải đợi hết nhịp — xem
        /// <see cref="IsWaitingOnReleasedGate"/> — nên con số chỉ quyết định độ mịn của bộ đếm, không quyết định độ trễ.
        /// </summary>
        private const float HostHoldPollSeconds = 1f / 60f;

        /// <summary>
        /// Đang đứng ở một nhịp chờ host mà cổng của nó đã được thả: nhịp đó xong ngay ở tick này, bất kể dt.
        ///
        /// <para>Trước đây lời thả chỉ được đọc khi nhịp 1/60 s hiện tại trôi hết, nên ở 120 Hz (ProMotion) hay 60 Hz mà dt
        /// dao động, cứ vài lần lại trễ một frame tuỳ pha của nhịp — đúng frame host đã huỷ proxy của nó vì tin model đổi ngay,
        /// và thanh hạng 4 biến mất một frame. Cổng Intro chỉ bị cắt ở nhịp CHỜ (sau <c>IntroWait</c>), không cắt đoạn Intro
        /// thật.</para>
        /// </summary>
        private bool IsWaitingOnReleasedGate =>
            (_phase == RevealPhase.PodiumHold && _podiumReleased) ||
            (_phase == RevealPhase.Intro && _isPollingHostHold && _hostReleased);

        /// <summary>
        /// Còn phải chờ host nữa không. Hết <c>HostHoldTimeout</c> thì thôi chờ và kêu đúng một lần.
        /// </summary>
        private bool ShouldKeepHoldingForHost()
        {
            if (!_settings.WaitForHostRelease) return false;
            if (_hostReleased) return false;

            if (_hostHoldElapsed >= Math.Max(0f, _settings.HostHoldTimeout))
            {
                HostHoldTimedOut = true;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Host báo đã diễn xong nhịp riêng của mình; bảng được phép xếp lại.
        ///
        /// <para>Gọi thừa, gọi sớm, gọi nhiều lần đều vô hại — cờ chỉ bật một chiều. Gọi TRƯỚC khi màn diễn
        /// bắt đầu cũng được: khi tới cuối pha Intro nó thấy cờ đã bật và đi thẳng.</para>
        ///
        /// <para>Đang chờ thì có hiệu lực ở <see cref="Tick"/> KẾ TIẾP, bất kể độ dài frame.</para>
        /// </summary>
        public void ReleaseHostHold()
        {
            _hostReleased = true;
        }

        /// <summary>
        /// Host báo đã diễn xong cú lên bục; row mình được đi nốt tới ô đích (pha PodiumClimb) rồi đáp.
        ///
        /// <para>Độc lập với <see cref="ReleaseHostHold"/> (cổng Intro): mỗi cổng một cờ, thả cổng này không mở cổng kia. Cũng
        /// một chiều như nó — gọi sớm (trước khi tới ranh giới: tới nơi là đi thẳng, nhịp PodiumTakeover vẫn phát), gọi thừa,
        /// gọi hai lần, gọi khi màn diễn không lên bục đều vô hại. Host nên gọi ở MỌI đường thoát của cú diễn của nó.</para>
        ///
        /// <para>Đang chờ ở cổng thì có hiệu lực ở <see cref="Tick"/> KẾ TIẾP, bất kể độ dài frame (60, 90, 120 Hz hay dt dao
        /// động): với <c>PodiumClimbDuration</c> = 0, tick đó đưa row mình tới ô đích và đặt những người bị vượt vào ô mới. Host
        /// gọi trước lượt Update của widget (DefaultExecutionOrder 110) thì đó là CÙNG frame — list vẽ trạng thái mới ở
        /// LateUpdate của frame ấy.</para>
        /// </summary>
        public void ReleasePodiumHold()
        {
            _podiumReleased = true;
        }

        /// <summary>
        /// Còn phải đứng ở cổng bục nữa không. Hết <c>HostHoldTimeout</c> thì thôi chờ và đặt cờ để lớp UI kêu.
        /// </summary>
        private bool ShouldKeepHoldingForPodium()
        {
            if (_podiumReleased) return false;
            if (_podiumHoldElapsed >= Math.Max(0f, _settings.HostHoldTimeout))
            {
                PodiumHoldTimedOut = true;
                return false;
            }
            return true;
        }

        private void BeginAfterIntro()
        {
            switch (Change.Kind)
            {
                case RankChangeKind.RankUp:
                    Emit(LeaderboardBeat.Lift, 0f);
                    // Dòng lên hạng chạy suốt từ lúc nhấc tới lúc đáp. View nào không vẽ nó thì mốc này vô hại.
                    _local.StartRankUpStream(_model.Clock);
                    if (_settings.GlowFadeInDuration > 0f) _local.StartGlowEnvelope(_settings.GlowFadeInDuration);
                    StartScoreCount(Change.FromScore, Change.ToScore);
                    FlipRankEarlyIfAsked();
                    EnterPhase(RevealPhase.Lift, _settings.LiftDuration);
                    break;
                case RankChangeKind.NewEntry:
                    BeginPop();
                    break;
                case RankChangeKind.ScoreImproved:
                    HasReachedLanding = true;
                    if (_settings.ScoreImprovedPill) _local.StartPill(PillContent.Best, 0, _tier, _model.Clock, _settings);
                    Emit(LeaderboardBeat.ScoreImproved, 0f);
                    StartScoreCount(Change.FromScore, Change.ToScore);
                    // Host tự đếm (và đã đếm xong trong lúc giữ cổng) ⇒ không còn gì để chờ ở pha đếm.
                    EnterPhase(RevealPhase.CountScore,
                               _settings.HostOwnsScoreCount && _settings.QuietPulseInsteadOfBob ? 0f : _settings.ScoreCountDuration);
                    break;
                default:
                    HasReachedLanding = true;
                    // Nhịp nhẹ (QuietPulseInsteadOfBob) chỉ dành cho lượt CÓ điểm mà không đổi hạng; lượt 0 điểm (Unchanged) thì
                    // bảng đứng yên — "chốt trạng thái cuối, hiện Continue ngay". Row mình đang trên bục (host trình bày) thì
                    // cú nhún không ai thấy — HostPresentedRowSkipsQuietPulse bỏ luôn.
                    EnterPhase(RevealPhase.Bob,
                               SkipsHostPresentedPulse ||
                               (_settings.QuietPulseInsteadOfBob && Change.Kind == RankChangeKind.Unchanged)
                                   ? 0f
                                   : BobPhaseDuration);
                    break;
            }
        }

        /// <summary>
        /// Đổi số hạng NGAY đầu màn diễn khi <see cref="MotionSettings.RankFlipsBeforeRankMove"/> bật.
        ///
        /// <para>Chỉ đụng con SỐ và cái pill, không đụng <c>Slot</c>: row vẫn phải bò lên như cũ, vì chính cú
        /// bò đó là thứ làm danh sách cuộn và hàng xóm đổi. Đổi cả Slot ở đây là row nhảy cóc một phát tới
        /// đích, mất sạch chuyển động.</para>
        ///
        /// <para><see cref="BeginLand"/> vẫn gọi lại <c>SetDisplayRank</c> lúc đáp — lặp lại có chủ đích, vì
        /// nó là đường duy nhất đúng khi cờ này TẮT, và khi cờ bật thì nó chỉ ghi đè cùng một giá trị.</para>
        /// </summary>
        private void FlipRankEarlyIfAsked()
        {
            if (!_settings.RankFlipsBeforeRankMove) return;

            _local.SetDisplayRank(Change.ToRank, _model.Clock, _settings, _model.TierRule, true);

            // Clip tham chiếu, khung 10,44: số hạng vừa lật thì sao vàng bung và mũi tên ▲ nổi lên NGAY — không
            // đợi tới lúc danh sách cuộn xong. Để chúng ở BeginLand là sao nở ra sau mũi tên cả giây, ngược
            // thứ tự bản gốc.
            PlayLandingFeedback();
        }

        /// <summary>
        /// Pill ▲N + sao vàng + flash + shine — "khoảnh khắc được lên hạng". Chạy đúng MỘT lần mỗi màn diễn.
        ///
        /// <para>Hai chỗ gọi: <see cref="FlipRankEarlyIfAsked"/> (khi lật sớm) và <see cref="BeginLand"/>
        /// (đường cũ, và đường bỏ qua trong lúc host đang giữ cổng). Pill không được bật hai lần:
        /// <c>RowState.StartPill</c> đặt lại đồng hồ của pill từ đầu, nên lần thứ hai làm nó nảy lại giữa chừng
        /// đúng lúc đang nổi lên.</para>
        /// </summary>
        private void PlayLandingFeedback()
        {
            if (_landingFeedbackPlayed) return;
            _landingFeedbackPlayed = true;

            // Flash có thể được tách sang pha đáp (LandFlashAt ≥ 0) — xem MotionSettings.LandFlashAt. Clip tham chiếu:
            // ở cú lật hàng KHÔNG sáng lên (độ sáng đứng yên 0,48), nó bừng sáng lúc ĐÁP (0,48 → 0,63, đỉnh +0,25 s).
            if (_settings.LandFlashAt < 0f)
            {
                _local.FlashNow(_settings.LandFlashAlpha, _settings.FlashDuration, _settings.FlashRiseDuration,
                                _settings.FlashDecayPower);
            }
            if (_settings.RankUpShine) _local.StartShine(_model.Clock, _settings);
            if (_settings.RankUpPill)
            {
                _local.StartPill(PillContent.RankUp, _plan != null ? _plan.PassedTotal : 0, _tier,
                                 _model.Clock, _settings);
            }
            // Sao có thể được dời sang pha đáp (LandTwinklesAt ≥ 0) — clip tham chiếu: sao nở lúc row CHẠM chỗ
            // mới, không phải lúc số hạng vừa lật.
            if (_settings.LandTwinklesAt < 0f) _listener.OnLanded(_tier, _local);
        }

        private void BeginSpin()
        {
            _lastSpinRank = _local.DisplayRank;
            EnterPhase(RevealPhase.Spin, _settings.SpinDuration);
        }

        private void BeginClimb()
        {
            _climbTicksEmitted = 0;
            EnterPhase(RevealPhase.Climb, RankUpPlanner.ClimbDuration(_listPassCount, _settings));
        }

        /// <summary>
        /// Nhịp tick theo đồng hồ của cú leo (<c>MotionSettings.ClimbTickInterval</c>): tick thứ k ở giây k × interval, chỉ khi
        /// k × interval &lt; max(interval, thời lượng − interval). Frame dài bỏ qua các tick đã lỡ
        /// thay vì dồn chúng vào một frame (một tràng tiếng chồng nhau tệ hơn thiếu một tiếng).
        /// </summary>
        private void EmitDueClimbTick()
        {
            float interval = _settings.ClimbTickInterval;
            float window = Math.Max(interval, _phaseDuration - interval);
            int total = (int)Math.Ceiling(window / interval - 0.0001f);
            if (_settings.CoroutineFrameTiming)
            {
                EmitClimbTickRearmed(interval, total);
                return;
            }
            int due = Math.Min(total, (int)Math.Floor(_phaseElapsed / interval + 0.0001f) + 1);
            if (due <= _climbTicksEmitted) return;
            _climbTicksEmitted = due;
            Emit(LeaderboardBeat.Pass, _phaseDuration <= 0f ? 1f : Easing.Clamp01(_phaseElapsed / _phaseDuration));
        }

        /// <summary>
        /// Nhịp tick khi <see cref="MotionSettings.CoroutineFrameTiming"/> bật: tick đầu ở khung đầu của pha, mỗi tick sau ở khung
        /// đầu tiên cách khung của tick trước ≥ <paramref name="interval"/> — đồng hồ hẹn lại từ khung nó nổ, nên ở tốc độ khung
        /// đều mỗi khoảng làm tròn LÊN số khung (0,18 s ở 60 Hz = 11 khung) thay vì dồn đều theo giây. Đồng hồ đếm từ khung đầu
        /// của pha, không gồm độ dài khung ấy. Tổng số tick không đổi.
        /// </summary>
        private void EmitClimbTickRearmed(float interval, int total)
        {
            if (_climbTicksEmitted >= total) return;
            float clock = _phaseElapsed - _phaseFirstFrameDelta;
            if (_settings.ClimbTickFrameRate > 0f)
            {
                // Lưới khung (MotionSettings.ClimbTickFrameRate): tick thứ k ở mốc k × khoảng-làm-tròn-lên-khung, nổ ở khung gần nhất.
                float frame = 1f / _settings.ClimbTickFrameRate;
                float gridInterval = (float)Math.Ceiling(interval * _settings.ClimbTickFrameRate - 0.0001f) * frame;
                if (clock < _climbTicksEmitted * gridInterval - frame * 0.5f) return;
                _climbTicksEmitted++;
                _lastClimbTickClock = clock;
                Emit(LeaderboardBeat.Pass, _phaseDuration <= 0f ? 1f : Easing.Clamp01(_phaseElapsed / _phaseDuration));
                return;
            }
            if (_climbTicksEmitted > 0 && clock - _lastClimbTickClock < interval - 0.0001f) return;
            _climbTicksEmitted++;
            _lastClimbTickClock = clock;
            Emit(LeaderboardBeat.Pass, _phaseDuration <= 0f ? 1f : Easing.Clamp01(_phaseElapsed / _phaseDuration));
        }

        /// <summary>Thời lượng pha Bob: cú nhún sin, hoặc nhấc + đáp khi <c>QuietPulseInsteadOfBob</c> bật.</summary>
        private float BobPhaseDuration => _settings.QuietPulseInsteadOfBob
            ? Math.Max(0f, _settings.LiftDuration) + Math.Max(0f, _settings.LandDuration)
            : _settings.BobDuration;

        /// <summary>
        /// Pha Bob dài 0 giây vì row mình đang do host trình bày (<c>MotionSettings.HostPresentedRowSkipsQuietPulse</c>): nó nằm
        /// sau bục, cú nhún không ai thấy — hiệu ứng của host trên bục mới là thứ quyết định lúc màn diễn xong. Cờ tắt thì không
        /// đọc độ hiện diện, quỹ đạo y như cũ.
        /// </summary>
        private bool SkipsHostPresentedPulse =>
            _settings.HostPresentedRowSkipsQuietPulse && _model.ListPresence(_local) <= 0f;

        /// <summary>Cỡ của nhịp nhẹ tại giây <paramref name="elapsed"/>: nhấc theo LiftCurve rồi đáp theo LandCurve.</summary>
        private float QuietPulseScale(float elapsed)
        {
            float lift = Math.Max(0.0001f, _settings.LiftDuration);
            if (elapsed < _settings.LiftDuration)
            {
                float up = Easing.Clamp01(elapsed / lift);
                float upEased = _settings.LiftCurve != null ? _settings.LiftCurve.Evaluate(up) : Easing.OutCubic(up);
                return Easing.LerpUnclamped(1f, _settings.LiftScale, upEased);
            }
            float down = Easing.Clamp01((elapsed - _settings.LiftDuration) / Math.Max(0.0001f, _settings.LandDuration));
            float downEased = _settings.LandCurve != null ? _settings.LandCurve.Evaluate(down) : Easing.OutCubic(down);
            return Easing.LerpUnclamped(_settings.LiftScale, 1f, downEased);
        }

        /// <summary>
        /// Sau Lift / Spin: leo trong list nếu còn người để vượt trước ranh giới; không còn thì tới cổng bục (khi lên bục) hoặc
        /// đáp luôn. Cờ tắt thì <c>_listPassCount == AnimatedCount</c> và <c>TakesPodium == false</c> — đúng rẽ nhánh cũ. Có cú
        /// tiếp cận kiểu cuộn thì luôn đi qua nó, kể cả khi không còn ai để vượt (bắt đầu ở ô ranh giới: camera vẫn phải cuộn về
        /// đỉnh list trong đúng thời lượng của quãng cuộn).
        /// </summary>
        private void BeginClimbOrHold()
        {
            if (_hasPodiumApproach) BeginPodiumApproach();
            else if (_listPassCount > 0) BeginClimb();
            else if (TakesPodium) BeginPodiumHold();
            else BeginLand();
        }

        /// <summary>
        /// Cú tiếp cận bục kiểu cuộn (<c>MotionSettings.PodiumApproachScrollSpeed</c>): chính là pha Climb (cùng đường cong, cùng
        /// luật vượt, cùng nhịp tick theo đồng hồ) nhưng dài đúng quãng cuộn / tốc độ, không kẹp.
        /// </summary>
        private void BeginPodiumApproach()
        {
            _climbTicksEmitted = 0;
            EnterPhase(RevealPhase.Climb, PodiumApproachDuration);
        }

        /// <summary>
        /// Thời lượng cú tiếp cận = quãng cuộn từ chỗ canh giữa ô xuất phát về đỉnh list (<c>BoardModel.PodiumApproachStartScroll</c>)
        /// chia cho tốc độ — tuyến tính, KHÔNG kẹp (nhảy xa thì cuộn lâu, đúng như quãng đường). Không list nào báo quãng cuộn
        /// (model tự lái bằng tay) thì rơi về công thức leo thường theo số người vượt.
        /// </summary>
        private float PodiumApproachDuration
        {
            get
            {
                float startScroll = _model.PodiumApproachStartScroll;
                if (float.IsNaN(startScroll)) return RankUpPlanner.ClimbDuration(_listPassCount, _settings);
                // Dừng hụt (MotionSettings.PodiumApproachShortfallRows): chỉ quãng thật sự cuộn mới tính thời lượng.
                return (startScroll - _model.PodiumApproachEndScroll) / _settings.PodiumApproachScrollSpeed;
            }
        }

        /// <summary>
        /// Cú tiếp cận đã xong (tới ranh giới, bị bỏ qua hay đóng giữa chừng): camera ở nhà — tiến độ chốt 1. Không có cú tiếp
        /// cận thì không đụng vào model.
        /// </summary>
        private void EndPodiumApproach()
        {
            if (_hasPodiumApproach) _model.SetPodiumApproachProgress(1f);
        }

        /// <summary>
        /// Tới ranh giới: phát nhịp PodiumTakeover (đúng một lần — các lần tự gia hạn sau đó không đi qua đây) rồi đứng chờ.
        /// Pha mở đầu dài 0 giây để host thả cổng SỚM thì đi thẳng trong cùng frame, không mất một nhịp chờ nào.
        /// </summary>
        private void BeginPodiumHold()
        {
            _local.Slot = ListClimbEndSlot;
            Emit(LeaderboardBeat.PodiumTakeover, 0f);
            EnterPhase(RevealPhase.PodiumHold, 0f);
        }

        private void BeginPodiumClimb()
        {
            ReleaseDeferredApproachPasses();
            _podiumClimbFromSlot = _local.Slot;
            EnterPhase(RevealPhase.PodiumClimb, _settings.PodiumClimbDuration);
        }

        /// <summary>
        /// Host vừa thả cổng bục: những người bị vượt ở đoạn trong list mà còn đứng yên (<c>DeferPodiumApproachPasses</c>) xuống
        /// một ô NGAY tick này (<c>PodiumPassSlideDuration</c> = 0 thì đặt thẳng) và mang số hạng thật, không cuộn số, không nhịp
        /// Pass — cùng tick row mình tới đích và người trên bục đứng vào ô mới, nên bảng về trạng thái cuối trong một frame.
        /// </summary>
        private void ReleaseDeferredApproachPasses()
        {
            if (!TakesPodium || _deferredPasses.Count == 0) return;
            for (int index = 0; index < _deferredPasses.Count; index++)
            {
                RowState row = _deferredPasses[index];
                SlidePastPodiumRow(row);
                row.SetDisplayRankImmediate(row.Entry.Rank);
            }
            _deferredPasses.Clear();
        }

        /// <summary>
        /// Đoạn sau cổng bục có "diễn" không. <c>PodiumClimbDuration</c> = 0 là nhảy thẳng tới đích: hình đã do host diễn
        /// xong, nên người bị vượt ở đoạn này không cuộn số, không phát Pass — một tràng nhịp dồn vào đúng frame thả cổng chỉ
        /// là tiếng ồn chồng lên cú đáp của host.
        /// </summary>
        private bool IsPodiumClimbAnimated => _settings.PodiumClimbDuration > 0f;

        /// <summary>Đường cong của cú leo (dùng chung cho đoạn trong list và đoạn sau cổng bục).</summary>
        private float ClimbEased(float progress)
        {
            if (_settings.ClimbCurve != null) return _settings.ClimbCurve.Evaluate(progress);
            return Math.Abs(_settings.ClimbEasePower - 3f) < 0.0001f
                ? Easing.InOutCubic(progress)
                : Easing.InOutPower(progress, _settings.ClimbEasePower);
        }

        private void BeginLand()
        {
            CompleteScoreCount();
            _local.SetDisplayScore(Change.ToScore, _model.Clock, true);
            HasReachedLanding = true;
            _local.Slot = _plan.FinalSlot;
            _local.SetDisplayRank(Change.ToRank, _model.Clock, _settings, _model.TierRule, true);

            Emit(LeaderboardBeat.Land, 1f);
            _local.EndRankUpStream(_model.Clock);
            StartDeferredPassSlides();
            if (_local.IsGlowEnvelopeActive) _local.ReleaseGlowEnvelope(_settings.GlowFadeOutDelay, _settings.GlowFadeOutDuration);
            PlayLandingFeedback();       // no-op nếu đã diễn lúc lật sớm
            if (_tier != RankTier.Standard) Celebrate();

            _landFromScale = _local.Scale;
            _landFromLift = _local.Lift;
            EnterPhase(RevealPhase.Land, _settings.LandDuration);
        }

        private void BeginPop()
        {
            HasReachedLanding = true;
            Emit(LeaderboardBeat.NewEntry, 0f);
            if (_settings.NewEntryAccent)
            {
                _local.StartPill(PillContent.New, 0, _tier, _model.Clock, _settings);
                _local.StartShine(_model.Clock, _settings);
                _local.FlashNow(_settings.LandFlashAlpha, _settings.FlashDuration, _settings.FlashRiseDuration,
                                _settings.FlashDecayPower);
            }
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
            SettleFinalState(emitTailBeat: true, keepGlowEnvelope: true);
            _phase = RevealPhase.Finished;
            Emit(LeaderboardBeat.RevealFinished, 1f);
        }

        /// <param name="keepGlowEnvelope">
        /// Kết thúc bình thường: glow theo đồng hồ riêng (nếu có) được tắt dần nốt sau màn diễn. Đóng giữa chừng: tắt ngay.
        /// </param>
        private void SettleFinalState(bool emitTailBeat, bool keepGlowEnvelope)
        {
            CompleteScoreCount();
            _local.SetDisplayScore(Change.ToScore, _model.Clock, false);
            if (_plan != null)
            {
                CrossRemaining(false);
                SettleDeferredPasses();
                AppendTail(emitTailBeat);
            }
            if (_local.DisplayRank != Change.ToRank) _local.SetDisplayRankImmediate(Change.ToRank);
            EndPodiumApproach();
            _local.EndRankUpStream(_model.Clock);
            _local.Scale = 1f;
            _local.Lift = 0f;
            if (!keepGlowEnvelope || !_local.IsGlowEnvelopeActive)
            {
                _local.StopGlowEnvelope();
                _local.GlowBoost = 0f;
            }
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
            // Bỏ qua = coi như host đã thả cổng bục: màn diễn đi thẳng tới trạng thái cuối qua đường bỏ qua sẵn có, không
            // còn chỗ nào để đứng chờ.
            _podiumReleased = true;
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
                    SettleDeferredPasses();
                    _model.FinishAllTweens();
                    _local.SetDisplayRankImmediate(Change.ToRank);
                    EndPodiumApproach();
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
            if (IsDeferringPass(_crossedCount))
            {
                // Đứng yên, giữ số hạng cũ: dời một lần lúc đáp (StartDeferredPassSlides), đổi số lúc chốt (SettleDeferredPasses);
                // màn lên bục thì dời + đổi số cùng lúc ở tick host thả cổng (ReleaseDeferredApproachPasses).
                _deferredPasses.Add(passedRow);
            }
            else
            {
                if (TakesPodium && _crossedCount >= _listPassCount) SlidePastPodiumRow(passedRow);
                else passedRow.TweenSlot(passedRow.Slot + 1f, _settings.PassSlideDuration);
                passedRow.SetDisplayRank(passedRow.Entry.Rank, _model.Clock, _settings, _model.TierRule, animate);
            }

            // Lật số hạng từ đầu (RankFlipsBeforeRankMove) ⇒ số của row mình ĐÃ là hạng cuối. Ghi tiếp ở đây là
            // kéo nó về "hạng của người vừa vượt − 1" = 47, 46, … — con số vừa lật xong lại lăn ngược từng
            // nấc, và theo chiều "tụt hạng" của RowState. Hàng xóm thì VẪN phải đổi số như cũ: trong clip, ngay
            // sau cú lật, hàng phía trên vẫn ghi "47 Lex" và chỉ đổi khi bị vượt.
            if (!_settings.RankFlipsBeforeRankMove)
            {
                _local.SetDisplayRank(passedRow.Entry.Rank - 1, _model.Clock, _settings, _model.TierRule, animate);
            }
            _crossedCount++;
            // Tick theo đồng hồ (ClimbTickInterval > 0) thay cho tick theo từng người bị vượt.
            if (animate && _settings.ClimbTickInterval <= 0f &&
                _passThrottle.TryFire(_model.Clock, _settings.MinimumPassBeatInterval))
            {
                Emit(LeaderboardBeat.Pass, 0f);
            }
        }

        /// <summary>
        /// Người bị vượt thứ <paramref name="passIndex"/> có đứng yên chờ không.
        /// <list type="bullet">
        /// <item>Màn leo không lên bục: <c>DeferPassSlidesToLand</c> — chờ tới cú đáp.</item>
        /// <item>Màn lên bục: <c>DeferPodiumApproachPasses</c>, và chỉ những người bị vượt ở đoạn TRONG LIST (chỉ số &lt;
        /// số người vượt trong list) — chờ tới tick host thả cổng. Người trên bục bị vượt sau cổng luôn đi theo
        /// <c>PodiumPassSlideDuration</c> như cũ.</item>
        /// </list>
        /// Cả hai cờ tắt thì y như cũ: màn leo thường theo <c>DeferPassSlidesToLand</c>, màn lên bục không bao giờ chờ.
        /// </summary>
        private bool IsDeferringPass(int passIndex)
        {
            return TakesPodium
                ? _settings.DeferPodiumApproachPasses && passIndex < _listPassCount
                : _settings.DeferPassSlidesToLand;
        }

        /// <summary>Lúc đáp: mọi người bị vượt cùng dời xuống một ô, tuyến tính, trong <c>LandPassSlideDuration</c>.</summary>
        private void StartDeferredPassSlides()
        {
            if (_deferredSlidesStarted) return;
            _deferredSlidesStarted = true;
            for (int index = 0; index < _deferredPasses.Count; index++)
            {
                RowState row = _deferredPasses[index];
                if (_settings.LandPassSlideDuration > 0f) row.TweenSlot(row.Slot + 1f, _settings.LandPassSlideDuration, true);
                else row.Slot = row.Slot + 1f;
            }
        }

        /// <summary>
        /// Chốt người bị vượt: tới ô mới ngay (nếu chưa kịp dời — đóng/bỏ qua trước lúc đáp) và đổi sang số hạng thật, không
        /// cuộn — dữ liệu cuối được gắn lại một lượt sau cú đáp.
        /// </summary>
        private void SettleDeferredPasses()
        {
            if (_deferredPasses.Count == 0) return;
            bool moveNow = !_deferredSlidesStarted;
            _deferredSlidesStarted = true;
            for (int index = 0; index < _deferredPasses.Count; index++)
            {
                RowState row = _deferredPasses[index];
                if (moveNow) row.Slot = row.Slot + 1f;
                row.SetDisplayRankImmediate(row.Entry.Rank);
            }
            _deferredPasses.Clear();
        }

        /// <summary>
        /// Người bị vượt ở đoạn sau cổng bục trượt xuống một ô theo <c>PodiumPassSlideDuration</c>; 0 = đặt thẳng ngay
        /// trong frame này (xem <see cref="MotionSettings.PodiumPassSlideDuration"/>).
        /// </summary>
        private void SlidePastPodiumRow(RowState passedRow)
        {
            if (_settings.PodiumPassSlideDuration > 0f) passedRow.TweenSlot(passedRow.Slot + 1f, _settings.PodiumPassSlideDuration);
            else passedRow.Slot = passedRow.Slot + 1f;
        }

        private void CrossRemaining(bool animate)
        {
            if (_plan == null) return;
            _local.Slot = _plan.FinalSlot;
            while (_crossedCount < _plan.AnimatedCount) CrossNext(animate);
        }

        /// <summary>Như <see cref="CrossRemaining"/> nhưng chỉ tới người thứ <paramref name="count"/> (đoạn leo trong list khi lên bục).</summary>
        private void CrossUpTo(int count, bool animate)
        {
            _local.Slot = _plan.StartSlot - count + ApproachStopOffset;
            while (_crossedCount < count) CrossNext(animate);
        }

        /// <summary>
        /// Chỗ dừng lệch khỏi ô ranh giới của cú tiếp cận (<c>MotionSettings.PodiumApproachStopOffsetRows</c>, cộng quãng hụt
        /// <c>PodiumApproachShortfallRows</c> khi list báo cú tiếp cận dừng hụt — camera dừng thấp hơn đỉnh đúng quãng đó nên row
        /// phải dời theo để trên màn vẫn dừng đúng chỗ); 0 khi màn này không có cú tiếp cận kiểu cuộn — mọi đường khác y như cũ.
        /// </summary>
        private float ApproachStopOffset => _hasPodiumApproach
            ? _settings.PodiumApproachStopOffsetRows + (_model.PodiumApproachStopsShort ? _settings.PodiumApproachShortfallRows : 0f)
            : 0f;

        /// <summary>Slot của row mình ở cuối đoạn leo trong list: ô cuối (hay ô ranh giới khi lên bục) + chỗ dừng lệch của cú tiếp cận.</summary>
        private float ListClimbEndSlot => _plan.StartSlot - _listPassCount + ApproachStopOffset;

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
            // Host tự đếm ⇒ timeline không được chạm vào con số, KỂ CẢ lần ghi giá trị đầu: host có thể đã
            // nhích bộ đếm lên rồi (vật bay đầu tiên đáp trước khi timeline kịp bắt đầu), và một lần ghi
            // "về FromScore" ở đây sẽ giật con số lùi lại ngay trước mắt người chơi.
            if (_settings.HostOwnsScoreCount)
            {
                _isScoreCounting = false;
                _scoreFrom = from;
                _scoreTo = to;
                return;
            }

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
