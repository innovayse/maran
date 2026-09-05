namespace Maran.Modules.Cron.Common;

/// <summary>What one cron entry's most recent run left behind.</summary>
/// <remarks>
/// Every field is nullable, and each null means "the agent reported none" rather than a value. That
/// distinction is the whole reason this type exists separately from <see cref="CronEntryDto"/>: an
/// empty string is a run that printed nothing, a zero exit code is a run that succeeded, and a zero
/// timestamp is the epoch — so none of the three defaults can stand in for absence without telling a
/// customer their job ran when it never has.
///
/// The output and the status both live under the account's home and the account can write them, so
/// this is what the ACCOUNT'S runs left behind. It is informational, never evidence of what the
/// agent did.
/// </remarks>
/// <param name="EntryId">The entry this reading belongs to, echoed so a response identifies itself.</param>
/// <param name="Output">
/// The tail of the run's output, bounded by the agent, or null when the agent reported none. It is
/// the customer's own program's output and is shown to them for the same reason the command is
/// (<see cref="CronEntryDto.Command"/>); it is equally absent from the panel's logs and journal.
/// </param>
/// <param name="LastExitCode">The exit status of the most recent run, or null when none was reported.</param>
/// <param name="LastRunAtUnix">
/// When the most recent run finished, in Unix seconds (UTC), or null when none was reported. Left as
/// seconds rather than converted: the panel renders times in the request's culture and time zone,
/// and a conversion this far from the reader would fix an interpretation nobody asked for.
/// </param>
public sealed record CronEntryOutputDto(
    string EntryId,
    string? Output,
    int? LastExitCode,
    long? LastRunAtUnix);
