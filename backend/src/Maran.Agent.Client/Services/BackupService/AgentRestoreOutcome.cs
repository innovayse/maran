namespace Maran.Agent.Client.Services.BackupService;

/// <summary>What a restore actually did.</summary>
/// <remarks>
/// <para>
/// There is no <c>bool Ok</c> here and no member that sums the others up: a caller states a verdict
/// by comparing <see cref="DatabasesRestored"/> against <see cref="DatabasesTotal"/> and reading
/// <see cref="FilesRestored"/> beside them. A success value that was one field is how an
/// account-deletion cascade once reported completion over rows it never touched.
/// </para>
/// <para>
/// The wire's <c>not_rolled_back</c> list is deliberately not carried here. It is documented as
/// ALWAYS EMPTY on the success message — a restore that reaches its ok message replaced every
/// database it dropped — and the lists that are not empty ride on the failing path, named in the
/// agent's error text. Copying a field the agent never fills would give the panel a member that
/// looks like a measurement and is a constant, which is the same defect as reading a deprecated
/// cron field's zero as a run result.
/// </para>
/// </remarks>
/// <param name="FilesRestored">Whether the account's home is now the archive's home.</param>
/// <param name="DatabasesRestored">How many databases were dropped, re-created and loaded.</param>
/// <param name="DatabasesTotal">
/// How many databases the restore set out to replace — every database the manifest names, after the
/// allowed list was applied. Equal to <see cref="DatabasesRestored"/> on a whole restore and larger
/// on every other.
/// </param>
public sealed record AgentRestoreOutcome(bool FilesRestored, uint DatabasesRestored, uint DatabasesTotal);
