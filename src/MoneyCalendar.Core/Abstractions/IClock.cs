namespace MoneyCalendar.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    DateOnly Today { get; }
}
