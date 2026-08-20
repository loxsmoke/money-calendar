using MoneyCalendar.Core.Entities;

namespace MoneyCalendar.Core.Abstractions;

/// <summary>
/// The read side of the ledger. Where <see cref="IEntryRepository"/> returns what is stored —
/// one row per one-off entry, one row per repeating series — this returns what the user should
/// see for a range: every one-off entry in it, plus each series expanded into the occurrences
/// that land inside it.
///
/// Every screen reads through this, so a repeating entry shows up in the calendar, the chart
/// and the lists without any of them knowing about recurrence.
/// </summary>
public interface IEntryQueryService
{
    /// <summary>
    /// Entries visible in the filter's range, newest first. The filter must carry a range or a
    /// day; without one, only stored rows are returned (series are not expanded into infinity).
    /// </summary>
    Task<IReadOnlyList<Entry>> GetAsync(EntryFilter filter, CancellationToken ct);
}
