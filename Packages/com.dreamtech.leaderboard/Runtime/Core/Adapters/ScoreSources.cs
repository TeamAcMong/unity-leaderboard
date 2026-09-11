using System;

namespace DreamTech.Leaderboard
{
    /// <summary>Nguồn điểm do code đặt trực tiếp (test, cheat, game tự quản điểm).</summary>
    public sealed class ManualScoreSource : IScoreSource
    {
        public ManualScoreSource(string metricId)
        {
            MetricId = metricId ?? string.Empty;
        }

        public string MetricId { get; }
        public bool HasScore { get; private set; }
        public long Score { get; private set; }

        public event Action ScoreChanged;

        public bool TryGetScore(out long score)
        {
            score = Score;
            return HasScore;
        }

        public void SetScore(long score)
        {
            HasScore = true;
            Score = score;
            ScoreChanged?.Invoke();
        }

        public void ClearScore()
        {
            HasScore = false;
            Score = 0;
            ScoreChanged?.Invoke();
        }
    }

    /// <summary>
    /// Nguồn điểm bọc một hàm đọc có sẵn của game — cách lắp nhanh nhất khi game đã giữ điểm ở chỗ khác.
    /// Hàm trả null nghĩa là chưa có điểm. Game gọi <see cref="NotifyScoreChanged"/> khi điểm đổi (không bắt buộc).
    /// </summary>
    public sealed class DelegateScoreSource : IScoreSource
    {
        private readonly Func<long?> _readScore;

        public DelegateScoreSource(string metricId, Func<long?> readScore)
        {
            MetricId = metricId ?? string.Empty;
            _readScore = readScore ?? throw new ArgumentNullException(nameof(readScore));
        }

        public string MetricId { get; }

        public event Action ScoreChanged;

        public bool TryGetScore(out long score)
        {
            long? value = _readScore();
            score = value.GetValueOrDefault();
            return value.HasValue;
        }

        public void NotifyScoreChanged()
        {
            ScoreChanged?.Invoke();
        }
    }
}
