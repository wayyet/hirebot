namespace HireBot.Repository.Extensions;

public static class DateTimeOffsetExtensions
{
    public static DateTimeOffset TruncateToMinute(this DateTimeOffset value)
    {
        return new DateTimeOffset(
            value.Year, value.Month, value.Day,
            value.Hour, value.Minute, 0,
            value.Offset);
    }
}
