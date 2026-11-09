namespace Maran.Modules.Backups.Common;

/// <summary>
/// Every closed set the backup screens select from, produced by the panel so the SPA holds no domain
/// constants and no translated vocabulary of its own.
/// </summary>
/// <param name="Statuses">The states a backup can be in, for the history filter.</param>
/// <param name="Kinds">The reasons a backup is taken, for the history filter.</param>
/// <param name="DestinationKinds">The kinds of storage a destination can name, for the destinations filter.</param>
/// <param name="Frequencies">The cadences a schedule can have.</param>
/// <param name="Weekdays">The weekdays a weekly schedule can fire on, in the order a week is read.</param>
/// <param name="Hours">The hours of the day (UTC) a schedule can name, <c>0</c> to <c>23</c>.</param>
/// <param name="RetainCounts">How many copies a schedule can keep, from the minimum to the ceiling.</param>
/// <param name="CustomerFrequencies">
/// The cadences a customer may choose for their own schedule: a subset of <see cref="Frequencies"/>.
/// </param>
/// <param name="CustomerRetainCounts">
/// How many copies a customer may keep on their own schedule, from the minimum to the customer
/// ceiling: a subset of <see cref="RetainCounts"/>.
/// </param>
public sealed record BackupOptionsDto(
    IReadOnlyList<BackupChoiceDto> Statuses,
    IReadOnlyList<BackupChoiceDto> Kinds,
    IReadOnlyList<BackupChoiceDto> DestinationKinds,
    IReadOnlyList<BackupChoiceDto> Frequencies,
    IReadOnlyList<BackupChoiceDto> Weekdays,
    IReadOnlyList<BackupChoiceDto> Hours,
    IReadOnlyList<BackupChoiceDto> RetainCounts,
    IReadOnlyList<BackupChoiceDto> CustomerFrequencies,
    IReadOnlyList<BackupChoiceDto> CustomerRetainCounts);
