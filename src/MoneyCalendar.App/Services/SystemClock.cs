using MoneyCalendar.Core.Abstractions;

namespace MoneyCalendar.App.Services;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
