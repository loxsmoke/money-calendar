using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;

namespace MoneyCalendar.Core.Services;

/// <inheritdoc cref="IEntryQueryService"/>
public sealed class EntryQueryService(IEntryRepository entries) : IEntryQueryService
{
    public async Task<IReadOnlyList<Entry>> GetAsync(EntryFilter filter, CancellationToken ct)
    {
        var window = filter.Day is { } day ? new DateRange(day, day) : filter.Range;

        // Series live at their start date, which is usually before the window, so the stored
        // rows have to be fetched without the date filter and dated by expansion instead.
        var stored = await entries.GetAsync(filter with { Range = null, Day = null }, ct)
            .ConfigureAwait(false);

        if (window is not { } range)
            return stored;

        var visible = new List<Entry>();
        foreach (var entry in stored)
        {
            if (!entry.IsRecurring)
            {
                if (range.Contains(entry.Date))
                    visible.Add(entry);
                continue;
            }

            foreach (var date in RecurrenceExpander.Occurrences(entry, range))
                visible.Add(entry.OccurrenceOn(date));
        }

        return visible
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.CreatedAt)
            .ToList();
    }
}
