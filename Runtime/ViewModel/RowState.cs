using System;

namespace DreamTech.Leaderboard.ViewModel
{
    /// <summary>
    /// Trạng thái HIỂN THỊ của một dòng, tách khỏi dữ liệu thật (<see cref="BoardRow"/>).
    ///
    /// <para>Vị trí là Slot kiểu float chứ không phải index: animation chỉ tween Slot, list ảo hoá tự đặt RectTransform theo Slot,
    /// nên row bị recycle vẫn đúng chỗ.</para>
    ///
    /// <para>MỌI trạng thái hiệu ứng (pill, vệt shine, cuộn số hạng, badge nảy, lần đổi điểm cuối) nằm ở đây thay vì trên view.
    /// Bản tham khảo giữ chúng trong coroutine của view nên mất sạch khi view bị thu hồi lúc skip.</para>
    /// </summary>
    public sealed class RowState
    {
        private TimedEffect _pillTiming;
        private PillContent _pillContent;
        private int _pillValue;
        private RankTier _pillTier;
        private TimedEffect _shineTiming;
        private TimedEffect _rankRollTiming;
        private int _rankRollPreviousRank;
        private int _rankRollDirection;
        private TimedEffect _badgePunchTiming;

        private float _slotFrom;
        private float _slotTo;
        private float _slotElapsed;
        private float _slotDuration;
        private bool _isSlotTweening;
        private bool _isSlotTweenLinear;

        private bool _isGlowEnvelopeActive;
        private float _glowFadeInDuration;
        private float _glowElapsed;
        private bool _isGlowReleased;
        private float _glowReleaseElapsed;
        private float _glowReleaseDelay;
        private float _glowReleaseDuration;
        private float _glowReleaseFrom;

        private float _introDelay;
        private float _introElapsed;
        private bool _isIntroPlaying;

        private float _flashDecayPeak;
        private float _flashDecayDuration = 0.0001f;
        private float _flashDecayElapsed;
        private float _flashDecayPower = 1f;
        private float _flashPeak;
        private float _flashRiseDuration;
        private float _flashRiseElapsed;
        private float _flashRiseFrom;
        private bool _isFlashRising;

        public RowState(BoardRow row, float slot)
        {
            Row = row;
            Slot = slot;
            DisplayRank = row.IsGap ? -1 : row.Entry.Rank;
            DisplayScore = row.IsGap ? 0 : row.Entry.Score;
            Scale = 1f;
            IntroAlpha = 1f;
            IntroScale = 1f;
            LastScoreChangeTime = double.NegativeInfinity;
        }

        public BoardRow Row { get; }
        public bool IsGap => Row.IsGap;
        public bool IsLocalPlayer => Row.IsLocalPlayer;

        /// <summary>Null khi là dòng "...".</summary>
        public LeaderboardEntry Entry => Row.Entry;

        /// <summary>Vị trí dọc theo đơn vị ô (0 = ô đầu); lẻ khi đang animate.</summary>
        public float Slot { get; set; }

        /// <summary>Hạng đang HIỂN THỊ (0-based). Khác Entry.Rank trong lúc diễn.</summary>
        public int DisplayRank { get; private set; }

        public long DisplayScore { get; private set; }

        public float Scale { get; set; }

        /// <summary>0..1 độ nhấc khỏi mặt list: điều khiển bóng đổ.</summary>
        public float Lift { get; set; }

        /// <summary>0..1: glow sáng hơn khi đang di chuyển.</summary>
        public float GlowBoost { get; set; }

        /// <summary>Lớp loé trắng, tự tắt dần.</summary>
        public float Flash { get; private set; }

        /// <summary>
        /// Độ loé tương đối (0..1) so với đỉnh của lần loé gần nhất — để thứ khác (viền glow) đi theo HÌNH của cú loé mà
        /// không cần biết đỉnh tuyệt đối là bao nhiêu.
        /// </summary>
        public float FlashLevel => _flashDecayPeak > 0f ? Math.Min(1f, Flash / _flashDecayPeak) : 0f;

        public float IntroAlpha { get; private set; }
        public float IntroOffset { get; private set; }

        /// <summary>Độ lùi NGANG còn lại của intro. Xem <c>MotionSettings.IntroOffsetX</c>.</summary>
        public float IntroOffsetX { get; private set; }
        public float IntroScale { get; private set; }

        public double LastScoreChangeTime { get; private set; }

        /// <summary>Tăng mỗi khi thứ ảnh hưởng chữ đổi (hạng, điểm, nội dung pill), để view chỉ ghi text khi cần.</summary>
        public int ContentVersion { get; private set; }

        public bool IsSlotTweening => _isSlotTweening;

        /// <summary>Glow đang chạy theo đồng hồ riêng (xem <c>MotionSettings.GlowFadeInDuration</c>) — timeline không ghi <see cref="GlowBoost"/>.</summary>
        public bool IsGlowEnvelopeActive => _isGlowEnvelopeActive;
        public bool IsIntroPlaying => _isIntroPlaying;

        /// <summary>Slot row sẽ dừng lại (đích của tween đang chạy, hoặc Slot hiện tại).</summary>
        public float TargetSlot => _isSlotTweening ? _slotTo : Slot;

        public TimedEffect PillTiming => _pillTiming;
        public PillContent PillContent => _pillContent;
        public int PillValue => _pillValue;
        public RankTier PillTier => _pillTier;
        public TimedEffect ShineTiming => _shineTiming;
        public TimedEffect RankRollTiming => _rankRollTiming;
        public int RankRollPreviousRank => _rankRollPreviousRank;

        /// <summary>+1 khi hạng tốt lên (số mới rơi từ trên xuống), -1 khi tụt.</summary>
        public int RankRollDirection => _rankRollDirection;

        public TimedEffect BadgePunchTiming => _badgePunchTiming;

        /// <summary>
        /// Mốc đồng hồ model lúc "dòng lên hạng" (mũi tên nổi trong row) bắt đầu; NaN = không có.
        /// Chỉ ghi THỜI ĐIỂM — cách vẽ (mấy mũi, bay nhanh chậm) là việc của view, nên hai view cùng render một
        /// row vẫn ra y hệt nhau.
        /// </summary>
        public double RankUpStreamStartTime { get; private set; } = double.NaN;

        /// <summary>Mốc dòng lên hạng thôi sinh mũi tên mới (lúc đáp); NaN = còn đang chạy hoặc không có.</summary>
        public double RankUpStreamEndTime { get; private set; } = double.NaN;

        public bool HasRankUpStream => !double.IsNaN(RankUpStreamStartTime);

        /// <summary>
        /// Host giành row này khỏi list NGAY từ frame này: list coi độ hiện diện là 0 (không view, kể cả khi đang ghim).
        /// Mặc định false.
        ///
        /// <para>Dùng khi host thay row bằng proxy của riêng nó đúng lúc proxy bắt đầu bay — ví dụ thanh của người chơi tách
        /// khỏi list để bay lên thành cờ hạng 3 trong lúc model vẫn giữ nó ở ô ranh giới chờ host thả cổng. Chỉ dựa vào Slot
        /// thì thanh gốc còn nằm yên đó suốt cú bay: hai hình của cùng một người trên màn hình.</para>
        ///
        /// <para>Cờ không tự tắt: host bật thì host tắt, ở MỌI đường thoát (xong, bỏ qua, đóng trang). Model là của một lần
        /// trình bày nên quên tắt cũng chỉ ảnh hưởng tới hết lần đó.</para>
        /// </summary>
        public bool IsHiddenFromList { get; set; }

        // ---------------------------------------------------------------- Hạng / điểm

        /// <summary>Đổi hạng hiển thị; khi animate thì cuộn số (có giới hạn tần suất khi quay số nhanh) và nảy badge nếu vào tầng huy chương cao hơn.</summary>
        public void SetDisplayRank(int rank, double now, MotionSettings settings, RankTierRule tierRule, bool animate)
        {
            if (rank == DisplayRank) return;
            int previous = DisplayRank;
            DisplayRank = rank;
            ContentVersion++;
            if (!animate || previous < 0 || rank < 0) return;

            bool rollThrottled = _rankRollTiming.IsStarted && now - _rankRollTiming.StartTime < settings.RankRollDuration * 0.9f;
            if (!rollThrottled)
            {
                _rankRollPreviousRank = previous;
                _rankRollDirection = rank < previous ? 1 : -1;
                _rankRollTiming.Start(now, settings.RankRollDuration);
            }

            int previousMedal = tierRule.MedalIndex(previous);
            int newMedal = tierRule.MedalIndex(rank);
            if (newMedal >= 0 && (previousMedal < 0 || newMedal < previousMedal)) _badgePunchTiming.Start(now, settings.BadgePunchDuration);
        }

        /// <summary>Đặt hạng không hiệu ứng (dựng trạng thái cũ, snap cuối).</summary>
        public void SetDisplayRankImmediate(int rank)
        {
            if (rank == DisplayRank) return;
            DisplayRank = rank;
            ContentVersion++;
            _rankRollTiming.Stop();
        }

        public void SetDisplayScore(long score, double now, bool animate)
        {
            if (score == DisplayScore) return;
            DisplayScore = score;
            ContentVersion++;
            if (animate) LastScoreChangeTime = now;
        }

        // ---------------------------------------------------------------- Hiệu ứng

        public void StartPill(PillContent content, int value, RankTier tier, double now, MotionSettings settings)
        {
            _pillContent = content;
            _pillValue = value;
            _pillTier = tier;
            _pillTiming.Start(now, settings.PillTotalDuration);
            ContentVersion++;
        }

        public void StopPill()
        {
            _pillTiming.Stop();
        }

        public void StartShine(double now, MotionSettings settings)
        {
            _shineTiming.Start(now, settings.ShineDuration);
        }

        public void FlashNow(float alpha, float duration)
        {
            FlashNow(alpha, duration, 0f, 1f);
        }

        public void FlashNow(float alpha, float duration, float riseDuration)
        {
            FlashNow(alpha, duration, riseDuration, 1f);
        }

        /// <summary>
        /// Loé sáng: lên tới <paramref name="alpha"/> trong <paramref name="riseDuration"/> giây (OutQuad; 0 = tức thì),
        /// rồi tắt trong <paramref name="duration"/> giây theo <c>(1 − p)^decayPower</c> (1 = tắt đều, như cũ).
        /// </summary>
        public void FlashNow(float alpha, float duration, float riseDuration, float decayPower)
        {
            _flashDecayPeak = alpha;
            _flashDecayDuration = Math.Max(0.0001f, duration);
            _flashDecayElapsed = 0f;
            _flashDecayPower = decayPower > 0f ? decayPower : 1f;
            if (riseDuration > 0f && alpha > Flash)
            {
                _flashPeak = alpha;
                _flashRiseDuration = riseDuration;
                _flashRiseElapsed = 0f;
                _flashRiseFrom = Flash;
                _isFlashRising = true;
                return;
            }
            _isFlashRising = false;
            Flash = alpha;
        }


        /// <summary>Bắt đầu dòng lên hạng (xem <see cref="RankUpStreamStartTime"/>).</summary>
        public void StartRankUpStream(double now)
        {
            RankUpStreamStartTime = now;
            RankUpStreamEndTime = double.NaN;
        }

        /// <summary>Thôi sinh mũi tên mới; những mũi đang bay tự tắt theo nhịp của view. Gọi thừa là vô hại.</summary>
        public void EndRankUpStream(double now)
        {
            if (!HasRankUpStream || !double.IsNaN(RankUpStreamEndTime)) return;
            RankUpStreamEndTime = now;
        }

        // ---------------------------------------------------------------- Tween nội bộ

        /// <summary>Trượt Slot tới vị trí mới, ease-out mượt (không nảy).</summary>
        public void TweenSlot(float target, float duration)
        {
            TweenSlot(target, duration, false);
        }

        /// <summary>Như trên; <paramref name="linear"/> = đi đều (ví dụ người bị vượt dời xuống một ô tuyến tính lúc đáp).</summary>
        public void TweenSlot(float target, float duration, bool linear)
        {
            _slotFrom = Slot;
            _slotTo = target;
            _slotElapsed = 0f;
            _slotDuration = Math.Max(0.0001f, duration);
            _isSlotTweening = true;
            _isSlotTweenLinear = linear;
        }

        /// <summary>
        /// Glow chạy theo đồng hồ riêng: hiện dần (smoothstep) trong <paramref name="fadeInDuration"/> giây rồi giữ sáng cho
        /// tới <see cref="ReleaseGlowEnvelope"/>.
        /// </summary>
        public void StartGlowEnvelope(float fadeInDuration)
        {
            _isGlowEnvelopeActive = true;
            _glowFadeInDuration = Math.Max(0.0001f, fadeInDuration);
            _glowElapsed = 0f;
            _isGlowReleased = false;
            GlowBoost = 0f;
        }

        /// <summary>Chờ <paramref name="delay"/> giây rồi tắt glow trong <paramref name="duration"/> giây (smoothstep).</summary>
        public void ReleaseGlowEnvelope(float delay, float duration)
        {
            if (!_isGlowEnvelopeActive || _isGlowReleased) return;
            _isGlowReleased = true;
            _glowReleaseElapsed = 0f;
            _glowReleaseDelay = Math.Max(0f, delay);
            _glowReleaseDuration = Math.Max(0.0001f, duration);
            _glowReleaseFrom = GlowBoost;
        }

        /// <summary>Bỏ ngay glow theo đồng hồ riêng (đóng trang, lỗi) — glow về 0.</summary>
        public void StopGlowEnvelope()
        {
            _isGlowEnvelopeActive = false;
            _isGlowReleased = false;
            GlowBoost = 0f;
        }

        private static float SmoothStep(float t)
        {
            t = Easing.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        public void StartIntro(float delay)
        {
            _isIntroPlaying = true;
            _introDelay = delay;
            _introElapsed = 0f;
            IntroAlpha = 0f;
        }

        public void FinishTweens()
        {
            if (_isSlotTweening)
            {
                Slot = _slotTo;
                _isSlotTweening = false;
            }
            if (_isIntroPlaying) FinishIntro();
        }

        public void Advance(float deltaTime, MotionSettings settings)
        {
            if (deltaTime <= 0f) return;

            if (_isSlotTweening)
            {
                _slotElapsed += deltaTime;
                float progress = Easing.Clamp01(_slotElapsed / _slotDuration);
                Slot = Easing.LerpUnclamped(_slotFrom, _slotTo, _isSlotTweenLinear ? progress : Easing.OutCubic(progress));
                if (progress >= 1f)
                {
                    Slot = _slotTo;
                    _isSlotTweening = false;
                }
            }

            if (_isIntroPlaying)
            {
                _introElapsed += deltaTime;
                float progress = Easing.Clamp01((_introElapsed - _introDelay) / Math.Max(0.0001f, settings.IntroDuration));
                float eased = Easing.OutCubic(progress);

                // Độ đục có nhịp RIÊNG khi được yêu cầu (xem MotionSettings.IntroFadeFraction). Mặc định 1 ⇒
                // fadeProgress == progress ⇒ y hệt hành vi cũ.
                float fadeFraction = settings.IntroFadeFraction > 0f ? settings.IntroFadeFraction : 1f;
                IntroAlpha = Easing.OutCubic(Easing.Clamp01(progress / fadeFraction));

                // Độ lùi có thể vượt quá chỗ đậu rồi bật về (MotionSettings.IntroSlideOvershoot). Mặc định 0 ⇒
                // vẫn là OutCubic như cũ, không vượt. Đường cong vẽ tay (IntroSlideCurve) thắng cả hai.
                float slide = settings.IntroSlideCurve != null
                    ? settings.IntroSlideCurve.Evaluate(progress)
                    : settings.IntroSlideOvershoot > 0f
                        ? Easing.OutBack(progress, settings.IntroSlideOvershoot)
                        : eased;
                IntroOffset = (1f - slide) * settings.IntroOffset;
                IntroOffsetX = (1f - slide) * settings.IntroOffsetX;
                IntroScale = Easing.LerpUnclamped(settings.IntroStartScale, 1f, Easing.OutBack(progress, settings.IntroOvershoot));
                if (progress >= 1f) FinishIntro();
            }

            if (_isGlowEnvelopeActive)
            {
                if (!_isGlowReleased)
                {
                    _glowElapsed += deltaTime;
                    GlowBoost = SmoothStep(_glowElapsed / _glowFadeInDuration);
                }
                else
                {
                    _glowReleaseElapsed += deltaTime;
                    float fade = SmoothStep((_glowReleaseElapsed - _glowReleaseDelay) / _glowReleaseDuration);
                    GlowBoost = _glowReleaseFrom * (1f - fade);
                    if (fade >= 1f) StopGlowEnvelope();
                }
            }

            if (_isFlashRising)
            {
                _flashRiseElapsed += deltaTime;
                float riseProgress = Easing.Clamp01(_flashRiseElapsed / Math.Max(0.0001f, _flashRiseDuration));
                Flash = Easing.LerpUnclamped(_flashRiseFrom, _flashPeak, Easing.OutQuad(riseProgress));
                if (riseProgress >= 1f) _isFlashRising = false;
            }
            else if (Flash > 0f)
            {
                _flashDecayElapsed += deltaTime;
                float remaining = 1f - Easing.Clamp01(_flashDecayElapsed / _flashDecayDuration);
                Flash = _flashDecayPeak * (Math.Abs(_flashDecayPower - 1f) < 0.0001f
                    ? remaining
                    : (float)Math.Pow(remaining, _flashDecayPower));
            }
        }

        private void FinishIntro()
        {
            _isIntroPlaying = false;
            IntroAlpha = 1f;
            IntroOffset = 0f;
            IntroOffsetX = 0f;
            IntroScale = 1f;
        }
    }
}
