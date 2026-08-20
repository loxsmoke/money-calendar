# Money Calendar — Application Specification

Version 0.1.0 · .NET 10 · Avalonia 12.1

This document describes what the application is, how it is built, what it stores, and every
calculation it performs. It is the reference for the implementation in `src/`; the
[README](../README.md) is the user-facing introduction.

---

## Contents

1. [Purpose and scope](#1-purpose-and-scope)
2. [Technology](#2-technology)
3. [Architecture](#3-architecture) — composition, navigation, window title
4. [Domain model](#4-domain-model) — entities, and how Settings presents categories
5. [Database design](#5-database-design)
6. [Calculations](#6-calculations)
7. [Import and export](#7-import-and-export)
8. [Validation rules](#8-validation-rules)
9. [Destructive operations](#9-destructive-operations)
10. [Files and settings](#10-files-and-settings) — multiple databases, locations, updates, CI
11. [Testing](#11-testing)
12. [Known limitations](#12-known-limitations)

---

## 1. Purpose and scope

Money Calendar is a single-user desktop application for **planning** income and expenses on a
calendar. Entries are typed in by hand, describe money that is expected as much as money that
has moved, and the app projects the running balance forward across the range being viewed.

**In scope:** hand-entered income and expenses, repeating series, named accounts, categories,
range summaries with a chart, JSON/CSV import and export, local storage.

**Out of scope, deliberately:** bank or provider integration, multi-user or sync, multi-currency,
budgets and goals, reconciliation against statements, attachments, encryption at rest. The one
network call in the app is an opt-out check for a newer release (§10.3); no telemetry, ever.

---

## 2. Technology

| Layer | Choice | Version |
|---|---|---|
| Runtime | .NET | `net10.0` |
| UI framework | Avalonia (Fluent theme, Inter fonts, DataGrid) | 12.1.0 |
| MVVM | CommunityToolkit.Mvvm (source-generated observables and commands) | 8.4.1 |
| Charting | LiveChartsCore.SkiaSharpView.Avalonia | 2.1.0-dev-798 |
| Hosting / DI | Microsoft.Extensions.Hosting | 10.0.10 |
| ORM | Microsoft.EntityFrameworkCore.Sqlite.Core | 10.0.10 |
| SQLite provider | SQLitePCLRaw.bundle_green / lib.e_sqlite3 | 2.1.11 / 3.53.3 |
| Logging | Serilog (rolling file sink) | 10.0.0 / 7.0.0 |
| Tests | xUnit + Avalonia.Headless | 2.9.3 / 12.1.0 |

Project-wide build settings live in `Directory.Build.props`: nullable enabled, implicit usings,
`TreatWarningsAsErrors`, and `InvariantGlobalization=false` (the app formats dates and numbers in
the user's culture). `NU1901`–`NU1904` are excluded from warnings-as-errors so the known SQLite
advisory on `SQLitePCLRaw.lib.e_sqlite3` does not fail the build.

Compiled bindings are on by default (`AvaloniaUseCompiledBindingsByDefault`), so every view
declares `x:DataType` and binding errors surface at build time rather than at runtime.

---

## 3. Architecture

Three projects, dependencies pointing inward:

```
MoneyCalendar.App  ──►  MoneyCalendar.Data  ──►  MoneyCalendar.Core
       │                                              ▲
       └──────────────────────────────────────────────┘
```

| Project | Contains |
|---|---|
| `MoneyCalendar.Core` | Entities, enums, repository and service abstractions, all calculation services (`SummaryService`, `EntryQueryService`, `RecurrenceExpander`, `RangePolicy`, `DataTransferService`), seed data. No UI and no EF Core. |
| `MoneyCalendar.Data` | `MoneyCalendarDbContext`, the three repositories, `DatabaseBootstrapper`, `DatabaseCatalog`, DI registration. Implements Core's abstractions. |
| `MoneyCalendar.App` | Avalonia shell, six page view models, dialogs, themes, formatting helpers, settings store. |

### Composition

`AppHost.Build()` is the composition root. It creates a `HostApplicationBuilder`, adds Serilog
writing to `%APPDATA%\MoneyCalendar\logs\money-calendar-.log` (daily rolling, 14 files retained),
then calls `AppHost.ConfigureServices(services, settingsStore, databasePath, seedSampleData)` —
the same method the headless UI tests call with a temporary database, which is why the test host
exercises the real object graph. The path comes from `AppDataPaths.DatabaseFor(settings.DatabaseName)`,
so the app reopens whichever database it was last switched to (§10.1). `seedSampleData` is passed
`false` and defaults to `false`, so nothing can seed a first run by omission.

Registration highlights:

- `IDbContextFactory<MoneyCalendarDbContext>` — repositories create a short-lived context per
  operation rather than holding one open, and the factory re-reads `options.DatabasePath` on every
  call. Those two facts together are what make switching databases at runtime possible without
  rebuilding the container (§10.1).
- Repositories, query and summary services, `IDataTransferService` and `IDatabaseCatalog` are
  singletons.
- **Page view models are singletons.** Range selections, the calendar month and scroll state
  survive navigation. `NavigationService` resolves them lazily from DI and caches them.
- `IClock` is injected everywhere a date is needed. Nothing in Core or Data calls `DateTime.Now`,
  which is what makes recurrence and range behaviour testable.

### Navigation

`PageKey` enumerates the six sections in shell order: `Summary`, `Income`, `Expenses`,
`Accounts`, `Settings`, `About`. `INavigationService` also carries:

- `NavigateToLedger(kind, range)` — the one deep link: a day or range picked in Summary opens
  Income or Expenses already scoped to it, via `RangePageViewModel.ApplyExternalRange`.
- `ReloadAllAsync()` — re-reads every cached page. Called after an import, an account or category
  change, and any bulk delete, so no page shows figures that no longer exist.

### Window title

The title bar reads `Money Calendar - 0.1.0`. The version is the **numeric** one only —
`SystemInfo.NumericVersion()` reads `Assembly.GetName().Version` and formats three parts — so a
prerelease suffix or a build sha cannot leak into it. About continues to show
`SystemInfo.AppVersion()`, which prefers the informational version; the two are deliberately
separate, because a bug report wants the full string and a title bar does not.

### Page lifecycle

`PageViewModel` owns load state (`IsLoading`, `IsEmpty`, `HasError`) and guards against
overlapping loads, so a rapid sequence of range changes never blanks the view mid-render.
`RangePageViewModel` adds the shared range selection used by Summary, Income and Expenses.

---

## 4. Domain model

### Entry

One dated income or expense, **or** the template of a repeating series.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `Date` | `DateOnly` | The entry's date, or the first day of the series |
| `Amount` | `decimal` | **Always a positive magnitude** |
| `Kind` | `EntryKind` | `Income` or `Expense` — carries the direction |
| `CategoryId` | `Guid` | Required; FK to `Categories` |
| `AccountId` | `Guid?` | Income: where the money lands. Expense: where it is paid from. Always an income-type account |
| `ToAccountId` | `Guid?` | Expenses only: the expense-type account it goes to. Null for income |
| `CurrencyCode` | `string(3)` | ISO 4217 |
| `AccountName`, `AccountLast4` | `string?` | Free-text label typed by hand ("Chase Sapphire", "4417"). Never a full account number |
| `Note` | `string?` | |
| `Frequency` | `RecurrenceFrequency` | `None` means a one-off |
| `DayOfMonth` | `int?` | Monthly: the day. Twice monthly: the first of two |
| `SecondDayOfMonth`, `SecondDayMode` | `int?`, `MonthDayMode` | Twice monthly: the second day |
| `Weekday` | `DayOfWeek?` | Weekly and bi-weekly |
| `RecurrenceEnd` | `DateOnly?` | Last day the series may land on; null runs indefinitely |
| `CreatedAt`, `UpdatedAt` | `DateTimeOffset` | |

Computed, not stored: `SignedAmount` (income positive, expense negative), `IsRecurring`
(`Frequency != None`), `IsOccurrence`.

**The amount convention matters.** Amounts are magnitudes and `Kind` carries direction, which
suits hand-entered rows: a user typing an expense should not have to type a minus sign, and a
sign error cannot silently turn an expense into income. Every aggregation that needs a signed
figure calls `SignedAmount` or subtracts explicitly. `EntryRepository` applies `Math.Abs` on
insert and update, so a negative amount cannot enter the database by any path, including import.

**A series is one row.** A repeating entry is stored once; the dates it lands on are computed on
read (§6.2). `OccurrenceOn(date)` produces a throwaway copy carrying the template's `Id` with
`IsOccurrence = true`. These copies are never tracked or saved — editing or deleting one acts on
the template it came from, which is why deleting one occurrence deletes the whole series.

### Account

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `Name` | `string(120)` | **Unique**, enforced by index |
| `Type` | `AccountType` | |
| `Last4` | `string(8)?` | |
| `Note` | `string(500)?` | |
| `CreatedAt`, `UpdatedAt` | `DateTimeOffset` | |

`AccountType` splits into two sides, and the split drives the entry pickers:

| Side | Types |
|---|---|
| **Income accounts** — money lands here | `Checking`, `Savings`, `Investment`, `OtherIncome` |
| **Expense accounts** — money goes here | `Credit`, `Mortgage`, `OtherExpense` |

`AccountTypes.IsIncome(type)` is the single source of that rule; the UI messages that name the
valid types are generated from the same list, so they cannot drift from what the pickers accept.

### Category

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Deterministic for built-ins, so seeding is idempotent |
| `Name` | `string(100)` | Unique **per kind** |
| `Kind` | `EntryKind` | A category belongs to income or to expenses, never both |
| `ColorHex` | `string(9)` | `#RRGGBB` |
| `IsSystem` | `bool` | Built-in: renameable and recolorable, never deletable |
| `WantsAccountDetails` | `bool` | Prompts for the account label and last digits (Credit card, Mortgage) |
| `SortOrder` | `int` | |

Built-in categories, all `IsSystem = true`:

- **Income:** Salary (10), Investment (20), Interest (30), Tips (40), Other (50)
- **Expense:** Rent (10), Utilities (20), Credit card (30, wants details), Mortgage (40, wants
  details), Fee (50), Groceries (60), Transport (70), Subscription (80), Other (999)

---

### Categories in the Settings screen

Settings splits the list in two, because the two halves are used differently:

| Panel | Holds | Groups start |
|---|---|---|
| **Custom categories** | `IsSystem = false` — the ones the user added | Expanded |
| **Built-in categories** | `IsSystem = true` — the 18 that ship | Collapsed |

Both are built from one `CategoryGroupTemplate` resource, so the row markup cannot drift apart,
and both group by kind into *Income types* and *Expense categories*. A kind with nothing in it is
left out rather than drawn as an empty group. **＋ Add category** lives on the custom panel, which
is where its result lands; the custom panel shows an empty-state line when nothing has been added.

Each group is an `Expander`. `CategoryGroup` carries an observable `IsExpanded` and a `CountText`
header ("8 categories") so a collapsed group still says what it is hiding. Because every category
edit triggers a reload that rebuilds the groups, expansion state would otherwise reset on each
rename — so each group reports changes to `SettingsViewModel`, which keeps them in a dictionary
keyed by group and restores them on rebuild.

The **Data** panel counts *custom* categories only (`CustomCategoryCountText`). The built-ins ship
with every install, so counting them there says nothing about what this ledger holds; a fresh
install reads `0`.

---

## 5. Database design

A single SQLite file. No EF migrations — see §5.4.

### 5.1 Storage conventions

| CLR type | Column type | Reason |
|---|---|---|
| `decimal` | `TEXT` | Round-trips full decimal precision. SQLite cannot compare or aggregate decimal TEXT, so **nothing is summed in SQL** — every aggregation happens in memory (§6). |
| `DateTimeOffset` | `INTEGER` | Unix epoch milliseconds, UTC, via a value converter, so comparisons translate to SQL. |
| `DateOnly` | `TEXT` | Provider-native `yyyy-MM-dd`. Sorts correctly and supports `Year`/`Month`/`Day` translation. |
| `enum` | `INTEGER` | Underlying value. |

Aggregating in memory is affordable because every screen queries a bounded range — at most 186
days (§6.1) — and the two unbounded reads (the opening balance and the account income totals)
walk rows the user typed by hand, not a provider feed.

### 5.2 Tables

**Entries**

| Column | Type | Null |
|---|---|---|
| `Id` | TEXT (PK) | no |
| `Date` | TEXT | no |
| `Amount` | TEXT | no |
| `Kind` | INTEGER | no |
| `CategoryId` | TEXT (FK) | no |
| `AccountId` | TEXT (FK) | yes |
| `ToAccountId` | TEXT (FK) | yes |
| `CurrencyCode` | TEXT(3) | no |
| `AccountName` | TEXT(120) | yes |
| `AccountLast4` | TEXT(8) | yes |
| `Note` | TEXT(500) | yes |
| `Frequency` | INTEGER | no (default 0) |
| `DayOfMonth` | INTEGER | yes |
| `SecondDayOfMonth` | INTEGER | yes |
| `SecondDayMode` | INTEGER | no (default 0) |
| `Weekday` | INTEGER | yes |
| `RecurrenceEnd` | TEXT | yes |
| `CreatedAt` | INTEGER | no |
| `UpdatedAt` | INTEGER | no |

Indexes: `Date`, `(Kind, Date)`, `CategoryId`, `AccountId`, `ToAccountId`, `Frequency`.

**Categories** — `Id` (PK), `Name` (100), `Kind`, `ColorHex` (9), `IsSystem`,
`WantsAccountDetails`, `SortOrder`. Unique index on `(Kind, Name)`.

**Accounts** — `Id` (PK), `Name` (120), `Type`, `Last4` (8), `Note` (500), `CreatedAt`,
`UpdatedAt`. Unique index on `Name`.

### 5.3 Relationships and delete behaviour

| Relationship | On delete | Rationale |
|---|---|---|
| `Entry.CategoryId → Categories.Id` | `Restrict` | A category holding entries cannot be deleted. The repository blocks it first with a message, so this is a belt-and-braces guard rather than a user-visible path. |
| `Entry.AccountId → Accounts.Id` | `SetNull` | Deleting an account leaves its entries in place, unlinked. The ledger is the record of what happened; the account list is naming. |
| `Entry.ToAccountId → Accounts.Id` | `SetNull` | Same. |

### 5.4 Creation, schema evolution and seeding

`DatabaseBootstrapper.InitializeAsync` runs at startup:

1. `SQLitePCL.Batteries_V2.Init()` and create the containing directory.
2. `EnsureCreatedAsync()` — builds the schema when the file is new. **There are no EF
   migrations.**
3. `EnsureLaterTablesAsync()` — because `EnsureCreated` only ever builds a *new* file's schema, a
   database from an earlier build would never receive later additions. Anything added after the
   first release is applied here by hand: `CREATE TABLE IF NOT EXISTS "Accounts"`, and an
   `ALTER TABLE ADD COLUMN` for each of `AccountId`, `ToAccountId`, `Frequency`, `DayOfMonth`,
   `SecondDayOfMonth`, `SecondDayMode`, `Weekday`, `RecurrenceEnd` that `PRAGMA table_info` shows
   missing. Column names come from a constant list in the source, never from user input.
4. `SeedCategoriesAsync()` — inserts any built-in category whose deterministic id is absent.
   Idempotent, and it repairs a database whose built-ins were removed by other means.
5. Sample data, only when the file was created in this run *and* `SeedSampleDataOnFirstRun` is
   set. **The desktop app never sets it**, so a first launch opens on an empty ledger with the
   built-in categories and nothing else; the demo data is opt-in from Settings (§6.8). The flag
   exists for the tests, which seed it deliberately.

---

## 6. Calculations

Everything in this section is implemented in `MoneyCalendar.Core` and covered by unit tests.

### 6.1 Range policy

`RangePolicy` holds the rules every section shares.

| Constant | Value | Meaning |
|---|---|---|
| `MaxDays` | 186 | Hard cap on any range (roughly six months) |
| `DailyBucketMaxDays` | 62 | Ranges up to this get one column per day |

- **`Clamp(from, to)`** — orders the endpoints if they arrive reversed, then trims the *tail* so
  the span is at most `MaxDays`: `to = from.AddDays(MaxDays - 1)`. The start is never moved, so
  clamping a hand-picked range keeps the end the user cares about most.
- **`BucketFor(range)`** — `Day` when `DayCount <= 62`, otherwise `Week`. This bounds the chart at
  about 62 columns whatever the range. Daily columns are what let the balance line step on the day
  money moves, so the threshold is set as wide as legibility allows rather than at a month.
- **`Buckets(range, size)`** — boundaries aligned to the **start of the range**, not to calendar
  weeks. Each is `[start, start + width - 1]`, and the final bucket is truncated to the range end,
  so a 10-day range in weekly mode yields buckets of 7 and 3 days.

`DayCount` is inclusive: `To.DayNumber - From.DayNumber + 1`.

The range dropdowns offer whole-month offsets: back is `This month` (0), `Last 2 months` (-1),
`Last 3 months` (-2), `Custom`; forward is `This month` (0), `Next 2 months` (+1), `Next 3 months`
(+2), `Custom`. A back offset resolves to the **first** day of that month, a forward offset to the
**last** day. Picking a date by hand switches that end — and only that end — to `Custom`. When the
selection exceeds `MaxDays`, a notice says the range was trimmed and to how many days.

### 6.2 Recurrence expansion

`RecurrenceExpander.Occurrences(template, range)` returns the dates a series lands on inside a
range, in ascending order. It is pure: no storage, no clock.

Window resolution first:

- Start at `max(template.Date, range.From)` — a series never produces a date before it begins.
- End at `min(template.RecurrenceEnd, range.To)` when the series has an end.
- If the resolved start is after the resolved end, there are no occurrences.
- A **one-off** entry yields its own date when the range contains it, and nothing otherwise.

Then, by frequency:

**Weekly (7 days) and bi-weekly (14 days).** The rhythm is anchored on the template's own date so
alternate weeks stay stable however the viewing range moves. If a `Weekday` is set, the anchor is
first shifted *forward* to it: `shift = ((int)weekday - (int)anchor.DayOfWeek + 7) % 7`. When the
anchor precedes the window, the expander jumps straight to the first occurrence at or after the
window start — `skips = ceil(elapsedDays / step)` — rather than stepping day by day. It then
emits every `step` days to the window end.

**Monthly.** For each calendar month touched by the window, resolve `DayOfMonth` (defaulting to
the template date's day) within that month and emit it if it falls inside the window and is not
before the template's start date.

**Twice monthly.** Same month walk, resolving two candidates per month: `DayOfMonth`, and the
second day per `SecondDayMode` — `OnDay` uses `SecondDayOfMonth`, `MidMonth` is always the 15th,
`LastDay` is whatever the month's last day is. Candidates are de-duplicated and ordered before
the same window and start-date filters apply.

**Short months.** A day past the end of a month lands on that month's last day
(`Math.Clamp(day, 1, DaysInMonth)`) rather than being skipped, so a series on the 31st occurs on
28 February — and on 29 February in a leap year.

`Describe(template)` renders the pattern in words for the UI — "Twice monthly on the 1st and the
last day, until Mar 3, 2027" — with English ordinals (`11th`–`13th` special-cased).

### 6.3 The read path

`EntryQueryService.GetAsync(filter)` is what every screen reads through, and it is the only place
that knows a series is not a row.

1. Take the window from `filter.Day` (as a single-day range) or `filter.Range`.
2. Query the repository with **the date filter removed** — a series lives at its start date, which
   is usually before the window, so a SQL date predicate would hide exactly the rows that need
   expanding. Kind, category and search predicates still run in SQL.
3. For each stored row: a one-off is kept when the window contains its date; a template is
   replaced by one `OccurrenceOn(date)` copy per occurrence in the window.
4. Sort descending by `Date`, then by `CreatedAt` to break ties deterministically.

With no window at all, stored rows are returned unexpanded — an open-ended series would otherwise
expand into infinity. This is what the Settings counts use, which is why a repeating series counts
as **one** transaction there however many occurrences it draws elsewhere.

Repository-level search (`EntryFilter.Search`) matches, case-insensitively via SQL `LIKE`, against
the note, the hand-typed account name and last four, the category name, and both linked account
names.

### 6.4 Summary aggregation

`SummaryService.GetRangeSummaryAsync(range)` produces everything the Summary section draws, from
one expanded read plus one history read.

1. **Per-day totals.** Group the expanded rows by date into `DayTotals(Date, Income, Expense,
   EntryCount)`, where income and expense are sums of magnitudes for that kind. Every date in the
   range gets an entry, zero-filled where nothing is recorded, so the chart has no gaps.
2. **Opening balance.** Sum `SignedAmount` over everything dated *before* the range —
   `[DateOnly.MinValue, range.From - 1]` — expanded the same way, so a monthly salary that started
   two years ago contributes every occurrence that has passed. The prototype has no configurable
   opening balance, so the line starts at zero before the first entry ever recorded.
3. **Buckets.** For each bucket, sum the day totals inside it, then carry a running balance:

   ```
   runningBalance += bucketIncome - bucketExpense
   BucketTotals(Start, End, Income, Expense, ClosingBalance: runningBalance)
   ```

   `ClosingBalance` is therefore the balance as of the bucket's **end**, including everything
   before the range.
4. **Range totals.** `TotalIncome` and `TotalExpense` are sums over all rows in the range;
   `Net = TotalIncome - TotalExpense`; `ClosingBalance = OpeningBalance + Net`.

**Chart construction** (`SummaryViewModel`): two column series (income `#1B7F3B`, expenses
`#C2603A`) and the balance line (`#2E9E52`) all share **one Y axis**. The bars carry no
`MaxBarWidth`, so each fills its half of the day's column, inset by `BarGap` pixels; that makes the
gap inside a day and the gap between one day's expense bar and the next day's income bar identical,
and puts the income bar on the day's left boundary where the balance line steps. All three series
share the axis, so the line's height can be
read against the bars directly. The balance is a **staircase**, not a sloped line: money arrives on
a day rather than accruing across the days between. `BalancePoints` emits it as explicit corners in
chart coordinates, and the level across a column is the balance **as that bucket opens** — what was
there before the day's money moved. So the first column sits at `OpeningBalance`, each riser stands
on the boundary where one bucket hands over to the next, and the last bucket's closing balance is a
final riser at the right-hand plot boundary. Levelling each column at its own *closing* balance
instead shifts the whole line one bucket earlier, opening the chart at a figure that only exists
once the first day is over. Spanning boundary to boundary also removes the blank half-column that
one-value-per-index would leave at each edge. A bar taller than the line is a bucket moving more
money than is left over. The axis floor is `min(0, lowest closing balance)`: columns never go below zero, but the
balance can, and a negative balance has to stay on the chart. The trade-off is deliberate — a
running total accumulated over a long range can tower over the per-bucket bars and flatten them
toward the baseline. Point markers are dropped above 20 buckets and x-labels rotate 45° above 12,
so a dense range stays legible. A hairline separator is drawn at every column boundary.

### 6.5 Ledger statistics (Income and Expenses)

Computed in `LedgerViewModel` from the rows currently on screen — so the figures always agree with
the list, including when the search box or the repeating-only filter has narrowed it.

| Figure | Calculation |
|---|---|
| Total | `rows.Sum(Amount)` |
| Entries | `rows.Count` |
| Average per day | `total / range.DayCount`, rounded to 2 decimals, `MidpointRounding.AwayFromZero`. Divides by the **range length**, not by the number of days that have entries, so an empty week pulls the average down. Zero when the range is empty |
| Largest | `rows.Max(Amount)`, or `—` when there are no rows |

**By-category breakdown.** Group by `CategoryId`; each item carries the sum and count, ordered by
amount descending then name (current culture). Two different ratios are computed per row:

- **Share** — `amount / sum of all groups`, formatted `P0`. Shows `—` when the total is zero.
- **Bar length** — `amount / largest group`, so the biggest category's bar is full width and the
  rest are drawn relative to it. This is deliberately *not* the share: shares of a long tail would
  render as invisible slivers.

### 6.6 Account income to date

Each account row shows what has come into it so far. The range is `[DateOnly.MinValue + 1 day,
clock.Today]` read through the query service, so repeating income is expanded and **every
occurrence that has already passed counts, while future-dated entries do not.** Rows are grouped
by `AccountId` and summed; accounts with no linked income show nothing rather than `$0.00`. The
group header shows the sum across the accounts of that type, and is blank when that sum is zero.

### 6.7 Formatting and rounding

- No rounding happens in storage or aggregation. `decimal` arithmetic runs at full precision and
  rounding is a display concern; the one explicit `decimal.Round` is the average-per-day figure.
- `Format.Money` renders `N2` in the current culture, prefixed by a symbol for `USD`, `EUR`, `GBP`
  and `CAD`, and suffixed by the raw code otherwise. Negative amounts take a leading `-` before
  the symbol; `explicitSign` adds `+` to positives, used for the net total.
- `Format.CompactMoney` is for calendar pills, where width is scarce: `$1.2k` from 1,000,
  `$12k` from 10,000 (one significant decimal, dropped when it is zero).
- Dates: `MMM d, yyyy` in lists, `ddd, MMM d, yyyy` where the weekday helps, `MMM d` on daily
  chart labels, and start-plus-end-day for weekly ones (`Aug 8–14`). Lists **sort by underlying
  value, not by rendered text**, so dates and amounts order correctly regardless of culture.

---

### 6.8 The demo ledger

`SampleData` is what a fresh database is seeded with and what Settings → **Load sample data**
adds. It is a *planning* ledger rather than a history, which is what makes the balance line worth
looking at on first launch:

| Part | Contents |
|---|---|
| Accounts | Demo Checking, Demo Savings, Demo Visa (••••1111), Demo Mastercard (••••2222), Demo Mortgage, Bill payments |
| Categories | Internet, Cell phone, Car payment — ordinary custom categories, deletable like any other |
| Coming in | Salary 2,000 twice monthly (1st and 15th); Interest 150 monthly (5th) |
| Standing bills | Mortgage 1,000 (2nd), Car payment 600 (6th), Utilities 80 (10th), Internet 50 (11th), Cell phone 45 (17th) |
| Still ahead | Two card payments (1,200 and 1,500) in the next few days, and rent of 1,300 and 1,400 in the following two months |

Everything runs through Demo Checking: income lands in it, expenses are paid from it into the
card, mortgage or bill-payments account.

Two properties make it safe to load repeatedly:

- **Dates are relative to `today`,** never to the calendar. The repeating series start on the first
  of last month, so there is a month of history behind the current one; the one-offs sit days and
  months ahead.
- **Every id is fixed** — accounts, categories and entries all use deterministic GUIDs — so it can
  never be loaded twice into the same ledger.

**Loading it is refused unless the ledger is empty.** `CanLoadSampleData` requires zero
transactions *and* zero accounts; the command throws otherwise and the message is reported as it
stands rather than logged as a failure. The reason is that the sample brings its own accounts and
files every entry through them: dropped on top of real books, there would be no telling the
invented entries from the real ones afterwards. *Delete all data* clears both counts, so the demo
is always reachable from a wiped ledger. The button stays enabled and explains the refusal, and the
Developer section shows a line saying why while the ledger holds anything.

---

## 7. Import and export

`DataTransferService` handles all four operations. `CurrentVersion = 1`.

### 7.1 JSON backup

The full-fidelity format: it round-trips accounts, categories and entries — including recurrence
patterns and account links — and is the supported way to carry data across a schema change.

```jsonc
{
  "version": 1,
  "exportedAt": "2026-08-19T10:32:00+00:00",
  "categories": [ { "id", "name", "kind", "colorHex", "isSystem", "wantsAccountDetails", "sortOrder" } ],
  "entries":    [ { "id", "date", "amount", "kind", "categoryId", "currencyCode",
                    "accountName", "accountLast4", "note", "accountId", "toAccountId",
                    "frequency", "dayOfMonth", "secondDayOfMonth", "secondDayMode",
                    "weekday", "recurrenceEnd" } ],
  "accounts":   [ { "id", "name", "type", "last4", "note" } ]
}
```

Serialization is pinned so backups are byte-identical across platforms and can be tested against
golden files: camelCase names, `\n` newlines, indented, enums as strings, nulls omitted.
Categories are ordered by kind, sort order then name; entries by date then id; accounts by type
then name.

On import: a file whose `version` is **greater** than `CurrentVersion` is rejected with a message
naming both versions. `accounts` is optional, so a v1 backup written before accounts existed still
imports — its entries simply arrive unlinked. Categories and accounts are inserted first via
`AddMissingAsync`, then entries are mapped, with two safety nets: an entry naming an unknown
category falls back to that kind's built-in **Other**, and an `AccountId`/`ToAccountId` that is
not present after the account insert is dropped to null rather than dangling.

### 7.2 CSV

A flat list of entries for spreadsheets. It does **not** carry recurrence or account links.

```
Date,Kind,Category,Amount,Currency,Account,AccountLast4,Note
```

Dates are ISO `yyyy-MM-dd`, invariant culture. Fields are escaped per RFC 4180 (`CsvText`), and
amounts are written invariant so a comma decimal separator cannot corrupt the file.

Import requires `Date`, `Kind`, `Category` and `Amount`; the rest are optional and matched by
header name, case-insensitively, in any column order. Per row:

- A date that is not ISO, or an amount that is not a number, aborts the import naming the line.
- `Kind` is read from its first letter (`i`/`e`). Anything else falls back to the **sign of the
  amount** — negative becomes an expense.
- An empty category becomes `Other`. An unknown `(kind, name)` pair creates a new custom category,
  colored from an eight-entry palette indexed by a hash of the name. .NET's string hashing is
  seeded per process, so the color is consistent within one import but not reproducible across
  runs — it is decoration, and the category can be recolored afterwards.
- The amount is stored as `Math.Abs`; the currency must be exactly three characters or it
  defaults to `USD`.

### 7.3 Merge and replace

| Mode | Behaviour |
|---|---|
| `Merge` | Adds what is missing. `AddRangeAsync` skips any entry whose id already exists, so re-importing the same backup is a no-op. |
| `Replace` | Deletes **every entry** first, then adds. Categories and accounts are kept either way. Confirmed in the UI before the file is read. |

`ImportResult(EntriesImported, EntriesSkipped, CategoriesImported)` reports the outcome;
`EntriesSkipped` is the count that already existed by id.

---

## 8. Validation rules

**Entry editor**

- A category must be picked.
- Income requires an income account. An expense requires both a from-account (income type) and a
  to-account (expense type). When the accounts needed do not exist, the section refuses to open
  the editor at all and instead names the missing side and its valid types — generated from
  `AccountTypes` — with a shortcut to the Accounts section.
- The amount must parse in the current culture or the invariant one, and must be greater than
  zero.
- `AccountLast4` must be at most four digits.
- A series' end date cannot precede its start.
- The two days of a twice-monthly series must differ.

**Account editor** — name required and unique (case-insensitively); type required.

**Category editor** — name required and unique within its kind.

**Database name** — `DatabaseCatalog.Validate`: 1–60 characters, no path separators or
`\ / : * ? " < > |`, no leading or trailing dot, and not a name already taken (§10.1).

---

## 9. Destructive operations

Nothing destructive happens without either a confirmation or an explicit rule that makes it safe.

| Action | Rule |
|---|---|
| Delete a category | Refused for built-ins. Refused while any entry is filed under it. Both refusals report which one applied. |
| Delete an account | An unused account is deleted after a confirmation. One in use offers to move its transactions to another account **on the same side** — income accounts take income accounts, expense accounts take expense accounts — and then deletes itself. With no eligible target, the delete is refused and says why. `ReassignAsync` repoints entries from either end and returns how many moved. |
| Delete an entry | Confirmed. Deleting one occurrence of a series deletes the whole series, and the confirmation says so. |
| **Delete all transactions** | Red button in Settings → Data. Requires typing `delete` (case-insensitive, surrounding spaces forgiven). Removes every entry; accounts, categories and settings are kept. |
| **Delete all data** | Red button beside it, same typed confirmation. Removes every entry **and every account**; categories and settings are kept. The dialog names both counts before it will fire. |
| Delete a database | Confirmed, naming the database and saying the file is removed from disk. **The database currently in use is refused** — switch to another first. Deleting one is the only action here that destroys data the JSON backup cannot recover, since it takes the file itself (§10.1). |
| Import in replace mode | Confirmed separately, naming the file, before the file is read. |

Both bulk deletes skip the typed confirmation when there is nothing to delete, reporting "There is
nothing to delete." instead of opening a dialog.

Going the other way, **Load sample data** is refused unless the ledger is completely empty (§6.8),
so the demo can never mix itself into real books.

---

## 10. Files and settings

### 10.1 Multiple databases

A database is one SQLite file in the data folder, and the app can hold as many as you like —
separate books, or a copy to try something destructive on. Switching between them lives in
**Settings → Developer**, deliberately: it changes what every screen is looking at, which is not a
control that belongs next to the theme picker.

`IDatabaseCatalog` (`MoneyCalendar.Data/DatabaseCatalog.cs`) is the whole feature:

| Operation | Behaviour |
|---|---|
| **List** | Every `*.db` in the folder, by name, with size and last-modified. The open one is marked *in use*. |
| **Select** | Drops SQLite's pooled handles, writes the new path into `MoneyCalendarDataOptions`, bootstraps the target, then reloads every page. The name is saved to `settings.json`, so the next launch opens the same one. |
| **New** | Creates an empty database — schema and built-in categories, never sample data — and leaves the app where it is. |
| **Clone** | Copies a database under a new name and opens the copy once to apply any later columns. Not switched to. |
| **Rename** | Moves the file, taking any `-wal`/`-shm` siblings with it. Renaming the open database updates both the options and the saved setting. |
| **Delete** | Removes the file and its siblings. **The open database is refused** — switch away first. |

Switching works at all because `MoneyCalendarDbContextFactory` reads `options.DatabasePath` on
every `CreateDbContext()` rather than capturing it, and because repositories hold a context only
for the length of one operation. Nothing has to be rebuilt in the DI container.

Names are file names, and `DatabaseCatalog.Validate` says so: 1–60 characters, no path separators
or `\ / : * ? " < > |`, no leading or trailing dot. A name already taken is refused. Every refusal
is an `InvalidOperationException` whose message is shown as it stands — this is the Developer
section, not a wizard.

`AppDataPaths.DatabaseFor(name)` resolves the saved name at startup and **falls back to the default
database when the file is gone**, so deleting a database from outside the app cannot stop it
starting.

### 10.2 Locations

```
%APPDATA%\MoneyCalendar\money-calendar.db      SQLite database — the default ledger
%APPDATA%\MoneyCalendar\*.db                   any other databases, listed in Settings → Developer
%APPDATA%\MoneyCalendar\settings.json          AppSettings
%APPDATA%\MoneyCalendar\logs\                  Serilog daily rolling files, 14 retained
%APPDATA%\MoneyCalendar\logs\startup-trace.log Pre-host startup trace
```

`AppDataPaths` resolves the root from `Environment.SpecialFolder.ApplicationData`, so the same
code lands in `~/.config` or `~/Library/Application Support` on other platforms.

`AppSettings` holds only preferences — `CurrencyCode` (default `USD`), `Theme` (`Default`, `Light`
or `Dark`, where `Default` follows the OS), `DatabaseName` (which database to open, §10.1) and
`CheckForUpdates` (§10.3). **No financial data is ever written to settings.** An unreadable settings file is logged and replaced with defaults rather than blocking
startup.

---

### 10.3 Update check

The About section asks GitHub for the latest release. It is the **only outbound request the app
ever makes**, it carries nothing about the machine or the ledger, and
`AppSettings.CheckForUpdates` turns it off entirely.

- `UpdateCheck` is the pure half: `ParseTag` reads a release tag as a version — a leading `v` is
  allowed and any `-rc1`/`+sha` suffix is dropped — and `IsNewer` compares it against the running
  build. Both **normalise to three parts**, because assembly versions carry a fourth (revision)
  that a tag never names, and `0.1.0.0` must not read as newer than `v0.1.0`.
- `UpdateService.CheckAsync` GETs `Brand.LatestReleaseApiUrl`, returns `null` unless the tag is
  newer, and otherwise reports the version, publish date, and the portable zip's URL and size.
  Every failure — offline, rate-limited, malformed — returns `null` and is logged at debug. An app
  that cannot reach GitHub is not a broken app.
- The asset it looks for is `UpdateCheck.PortableAssetName(version)`, which has to match the name
  the release workflow attaches. A test pins the string on the app's side.
- About checks quietly on arrival (saying nothing unless there is something newer) and **Check
  now** announces either way. Nothing is downloaded or installed: **Download** opens the zip's URL
  in a browser, because a portable release is something the user unpacks where they want it.

### 10.4 CI and releases

| Workflow | Trigger | Does |
|---|---|---|
| `.github/workflows/ci.yml` | Push and PR against `main` | Restore, build and test in Release on `windows-latest` |
| `.github/workflows/release.yml` | `workflow_dispatch` with a `version` input | Stamps the version, tests, publishes, tags and releases |

The release job validates the version (three numeric parts, optional pre-release suffix) and
refuses a tag that already exists on the remote **before** building. It then stamps `Version`,
`AssemblyVersion` and `FileVersion` into `MoneyCalendar.App.csproj` with `dotnet-property`, runs
the tests against the stamped build, and publishes self-contained single-file for `win-x64`.
Untrimmed, deliberately: Avalonia resolves views and converters reflectively.

The commit, the tag and the GitHub Release happen **only after the build succeeds**, so a failed
release leaves no version bump behind. A version with a pre-release suffix is marked as a
pre-release on GitHub. The one attached asset is
`MoneyCalendar-<version>-win-x64-portable.zip` — the name the update check looks for (§10.3).

Windows runners for both: the app is a `WinExe` with a Windows application manifest, and the
headless UI tests render through SkiaSharp against the same stack users run.

---

## 11. Testing

`dotnet test MoneyCalendar.slnx` — 181 tests, xUnit.

| Area | Coverage |
|---|---|
| `Data/RepositoryTests` | Entry CRUD, filters, search, bulk insert and delete; a new database opens holding built-in categories and nothing else |
| `Data/AccountRepositoryTests` | Uniqueness, usage counts, reassignment, the shape of the seeded accounts |
| `Data/DatabaseCatalogTests` | Listing, create, clone divergence, switching what the repositories read, rename-while-open, the delete refusals, name validation |
| `Services/RecurrenceExpanderTests` | Every frequency, weekday anchoring, short-month clamping, end dates, window edges |
| `Services/RangePolicyTests` | Clamping, reversed endpoints, the 62-day bucket boundary from both sides |
| `Services/SummaryServiceTests` | Day totals, zero-fill, opening balance, running balance across buckets, the demo ledger running in both directions |
| `Services/EntryQueryServiceTests` | Series expansion into a window, ordering, unwindowed reads |
| `Services/DataTransferServiceTests` | JSON round-trip, version rejection, CSV parsing and failure messages, merge vs replace |
| `Ui/ShellSmokeTests` | Headless Avalonia across all six sections: chart series and axes, the balance staircase, range defaults, account pickers, category management |
| `Ui/SettingsLayoutTests` | The Developer section's database panel end to end, sample-data rules, where destructive buttons sit |
| `Ui/DeleteAllDataTests` | Both delete buttons: styling, order, the two dialog wordings, what each actually removes |
| `Ui/ListSortingTests`, `Ui/DateFieldTests` | Sorting by value rather than rendered text; date entry |
| `Ui/WindowTitleTests` | The title format and that the version in it is numeric |
| `Services/UpdateCheckTests` | Tag parsing, what counts as newer, the assembly-revision case, the release asset name |

The UI tests build the real DI graph through `AppHost.ConfigureServices` against a temporary
database file, so they exercise production wiring rather than a parallel test-only composition.
`IClock` is substituted to keep date-dependent assertions stable, and `TestDatabase` reads its path
from the shared options exactly as the app's factory does, so database switching is exercised the
way it actually runs.

---

## 12. Known limitations

Deliberate simplifications, recorded so they are not mistaken for defects:

- **No encryption at rest.** The database is a plain local file. The JSON backup is the way to
  move data.
- **No EF migrations.** `EnsureCreated` plus the hand-written `EnsureLaterTablesAsync` step
  (§5.4). A structural schema change means a fresh database: export first, import after. This is
  why cloning opens the copy once — a database written by an older build is brought up to date on
  the way in, as far as added tables and columns can manage.
- **Single currency.** `AppSettings.CurrencyCode` exists and every amount carries its own code,
  but there is no UI to change it and no conversion anywhere; the app formats in USD.
- **No configurable opening balance.** The balance line starts from zero before the first entry
  ever recorded, so it reads as "net position since you started keeping this ledger" rather than a
  real bank balance.
- **Entries are not posted to accounts.** Accounts are named endpoints on an entry; there is no
  per-account ledger or reconciliation, and the only per-account figure is income to date (§6.6).
- **Switching databases is not guarded.** Anything mid-flight when a switch happens is reading the
  old file; in practice the UI runs one command at a time and every page is reloaded afterwards,
  but there is no lock enforcing it.
- **English-only ordinals.** `RecurrenceExpander.Describe` builds its text in English even though
  dates and numbers format in the user's culture.
- `SQLitePCLRaw.lib.e_sqlite3` carries the known NU1903 advisory, excluded from
  warnings-as-errors.
