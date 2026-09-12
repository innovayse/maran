namespace Maran.Modules.Backups.Common;

/// <summary>
/// What a restore that stopped partway had already replaced, published as the <c>restore</c>
/// extension member of the failure's problem response (the <c>ProblemExtension</c> convention,
/// <c>ApiResultExtensions</c>). The counts are the operator's measure of damage, and the failure
/// path is the one ending where they matter — a partial restore's <c>RestoreOutcomeDto</c> cannot
/// travel there, because a failed result carries no value.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately smaller than <see cref="RestoreOutcomeDto"/>: no <c>BackupId</c>/<c>AccountId</c>
/// (the caller named the backup themselves), no <c>Whole</c> (a failure response IS the verdict),
/// no <c>FailureCode</c> (the problem's own <c>code</c> member already carries it). Three facts the
/// agent measured, and nothing restated.
/// </para>
/// <para>
/// It is attached ONLY when the agent stated a terminal outcome (<c>RestoreRunOutcome.Measured</c>).
/// A truncated or refused run has counts of zero that are "the agent said nothing", not "nothing
/// was replaced", and publishing them would hand the operator a false <c>0 of 0</c>.
/// </para>
/// </remarks>
/// <param name="FilesRestored">Whether the account's home was already the archive's home when the run stopped.</param>
/// <param name="DatabasesRestored">How many databases had been dropped, re-created and loaded when it stopped.</param>
/// <param name="DatabasesTotal">How many the restore set out to replace.</param>
public sealed record RestorePartialDto(
    bool FilesRestored,
    uint DatabasesRestored,
    uint DatabasesTotal);
