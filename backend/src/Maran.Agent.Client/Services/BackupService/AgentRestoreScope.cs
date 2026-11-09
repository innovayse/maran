namespace Maran.Agent.Client.Services.BackupService;

/// <summary>What one restore replaces: the whole account, one database or one site (issue #160).</summary>
/// <remarks>
/// <para>
/// One of exactly three cases, each its own type, because they share nothing: the whole account
/// carries no member (<see cref="AgentWholeRestoreScope"/>), a database carries its full prefixed
/// name (<see cref="AgentDatabaseRestoreScope"/>) and a site carries its primary domain
/// (<see cref="AgentSiteRestoreScope"/>). A single record with an optional name and an optional
/// domain would let a caller build a scope naming both, or neither while meaning one.
/// </para>
/// <para>
/// <b>No case carries a path.</b> A site is named by its domain and the agent derives the directory
/// it occupies, so the panel cannot aim a restore at a directory of its own choosing, whatever a
/// caller above it supplied. The wire message stays a <c>oneof</c>; the cases are flattened onto it
/// in <c>AgentBackupClient</c>, the only place that knows the wire shape.
/// </para>
/// </remarks>
public abstract record AgentRestoreScope
{
    /// <summary>Closes the hierarchy to the three cases this client declares.</summary>
    private protected AgentRestoreScope()
    {
    }
}
