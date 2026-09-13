using System;
using System.Globalization;

namespace DreamTech.Leaderboard.League
{
    /// <summary>Lịch mùa: thời điểm nào thuộc mùa nào.</summary>
    public interface ISeasonSchedule
    {
        SeasonWindow GetSeasonAt(DateTime utc);
    }

    /// <summary>
    /// Mặc định: các mùa dài bằng nhau nối tiếp nhau tính từ một mốc (vd mốc thứ Hai 00:00 UTC + 7 ngày = mùa theo tuần).
    /// Id mùa = tiền tố + số thứ tự (âm nếu trước mốc) nên mọi máy, mọi lần cài lại đều ra cùng id.
    /// </summary>
    public sealed class FixedLengthSeasonSchedule : ISeasonSchedule
    {
        public const string DefaultIdPrefix = "season-";

        public FixedLengthSeasonSchedule(DateTime anchorUtc, TimeSpan seasonLength, string idPrefix = DefaultIdPrefix)
        {
            if (seasonLength <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(seasonLength), "Mùa phải dài hơn 0.");
            AnchorUtc = DateTime.SpecifyKind(anchorUtc, DateTimeKind.Utc);
            SeasonLength = seasonLength;
            IdPrefix = idPrefix ?? string.Empty;
        }

        public DateTime AnchorUtc { get; }
        public TimeSpan SeasonLength { get; }
        public string IdPrefix { get; }

        public SeasonWindow GetSeasonAt(DateTime utc)
        {
            long elapsedTicks = DateTime.SpecifyKind(utc, DateTimeKind.Utc).Ticks - AnchorUtc.Ticks;
            long lengthTicks = SeasonLength.Ticks;
            long seasonNumber = elapsedTicks >= 0 ? elapsedTicks / lengthTicks : -((-elapsedTicks + lengthTicks - 1) / lengthTicks);
            DateTime start = new DateTime(AnchorUtc.Ticks + seasonNumber * lengthTicks, DateTimeKind.Utc);
            return new SeasonWindow(IdPrefix + seasonNumber.ToString(CultureInfo.InvariantCulture), start, start + SeasonLength);
        }
    }
}
