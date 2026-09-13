using System;

namespace DreamTech.Leaderboard.League
{
    /// <summary>Một mùa: [StartUtc, EndUtc). Bất biến; hai mùa bằng nhau khi cùng id và cùng mốc.</summary>
    public sealed class SeasonWindow : IEquatable<SeasonWindow>
    {
        public SeasonWindow(string seasonId, DateTime startUtc, DateTime endUtc)
        {
            if (string.IsNullOrEmpty(seasonId)) throw new ArgumentException("Season id không được rỗng.", nameof(seasonId));
            if (endUtc <= startUtc) throw new ArgumentException("Mùa phải kết thúc sau khi bắt đầu.", nameof(endUtc));
            SeasonId = seasonId;
            StartUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
            EndUtc = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc);
        }

        public string SeasonId { get; }
        public DateTime StartUtc { get; }
        public DateTime EndUtc { get; }
        public TimeSpan Length => EndUtc - StartUtc;

        public bool HasEnded(DateTime nowUtc)
        {
            return nowUtc >= EndUtc;
        }

        public TimeSpan TimeLeft(DateTime nowUtc)
        {
            TimeSpan left = EndUtc - nowUtc;
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }

        /// <summary>0 lúc bắt đầu, 1 lúc kết thúc, kẹp trong [0, 1].</summary>
        public double Progress(DateTime nowUtc)
        {
            double progress = (nowUtc - StartUtc).TotalSeconds / Length.TotalSeconds;
            if (progress < 0) return 0;
            return progress > 1 ? 1 : progress;
        }

        public bool Equals(SeasonWindow other)
        {
            if (ReferenceEquals(other, null)) return false;
            return string.Equals(SeasonId, other.SeasonId, StringComparison.Ordinal) && StartUtc == other.StartUtc && EndUtc == other.EndUtc;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SeasonWindow);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (SeasonId.GetHashCode() * 397) ^ StartUtc.GetHashCode();
            }
        }

        public override string ToString()
        {
            return SeasonId + " [" + StartUtc.ToString("u") + " → " + EndUtc.ToString("u") + ")";
        }
    }
}
