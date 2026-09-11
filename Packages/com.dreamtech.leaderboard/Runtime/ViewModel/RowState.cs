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

        private float _introDelay;
        private float _introElapsed;
        private bool _isIntroPlaying;

        private float _flashDecayPerSecond;

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

        public float IntroAlpha { get; private set; }
        public float IntroOffset { get; private set; }
        public float IntroScale { get; private set; }

        public double LastScoreChangeTime { get; private set; }

        /// <summary>Tăng mỗi khi thứ ảnh hưởng chữ đổi (hạng, điểm, nội dung pill), để view chỉ ghi text khi cần.</summary>
        public int ContentVersion { get; private set; }

        public bool IsSlotTweening => _isSlotTweening;
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
            Flash = alpha;
            _flashDecayPerSecond = alpha / Math.Max(0.0001f, duration);
        }

        // ---------------------------------------------------------------- Tween nội bộ

        /// <summary>Trượt Slot tới vị trí mới, ease-out mượt (không nảy).</summary>
        public void TweenSlot(float target, float duration)
        {
            _slotFrom = Slot;
            _slotTo = target;
            _slotElapsed = 0f;
            _slotDuration = Math.Max(0.0001f, duration);
            _isSlotTweening = true;
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
                Slot = Easing.LerpUnclamped(_slotFrom, _slotTo, Easing.OutCubic(progress));
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
                IntroAlpha = eased;
                IntroOffset = (1f - eased) * settings.IntroOffset;
                IntroScale = Easing.LerpUnclamped(settings.IntroStartScale, 1f, Easing.OutBack(progress, settings.IntroOvershoot));
                if (progress >= 1f) FinishIntro();
            }

            if (Flash > 0f) Flash = Math.Max(0f, Flash - _flashDecayPerSecond * deltaTime);
        }

        private void FinishIntro()
        {
            _isIntroPlaying = false;
            IntroAlpha = 1f;
            IntroOffset = 0f;
            IntroScale = 1f;
        }
    }
}
