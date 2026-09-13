namespace Maran.Modules.Backups.Common;

/// <summary>Outward view of what a restore did.</summary>
/// <remarks>
/// <para>
/// It carries the verdict AND the three facts it was computed from, which looks redundant and is
/// not. The verdict is what a screen must branch on, and computing it in the SPA would be a second
/// statement of the rule in a place the backend cannot test (rules/architecture.md "The backend owns
/// the data, the SPA renders it"). The three facts are what a screen must SHOW: "eleven of twelve
/// databases" is the sentence an operator needs, and a bare "failed" would send them to the server
/// to find out how bad it is.
/// </para>
/// <para>
/// <b>There is no list of rolled-back databases here, and its absence is deliberate.</b> Those lists
/// live only on the agent's failing arms, inside diagnostic text that the agent client logs at its
/// own boundary and never carries outward — it can quote a database name and a path. The wire's
/// <c>not_rolled_back</c> field on the success message is documented as always empty, so a field
/// here would be a constant wearing the clothes of a measurement. What the panel gives an operator
/// instead is the failure CODE, which distinguishes a clean rollback from a partial one, and the
/// server's own log, which names the databases.
/// </para>
/// </remarks>
/// <param name="BackupId">The backup that was restored from.</param>
/// <param name="AccountId">The account that was replaced.</param>
/// <param name="Whole">
/// Whether the account was fully replaced. The only field a caller may read as success, and it is
/// <c>false</c> whenever anything at all was left behind.
/// </param>
/// <param name="FilesRestored">Whether the account's home is now the archive's home.</param>
/// <param name="DatabasesRestored">How many databases were dropped, re-created and loaded.</param>
/// <param name="DatabasesTotal">How many the restore set out to replace.</param>
/// <param name="FailureCode">
/// The machine-stable code naming what went wrong, or empty when <paramref name="Whole"/>. A partial
/// restore carries one, because it is not a success.
/// </param>
public sealed record RestoreOutcomeDto(
    Guid BackupId,
    Guid AccountId,
    bool Whole,
    bool FilesRestored,
    uint DatabasesRestored,
    uint DatabasesTotal,
    string FailureCode);
