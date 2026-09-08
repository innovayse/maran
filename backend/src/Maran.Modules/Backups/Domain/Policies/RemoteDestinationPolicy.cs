using Maran.Modules.Backups.Domain.Enums;

namespace Maran.Modules.Backups.Domain.Policies;

/// <summary>
/// The one answer to "which kinds of destination may this build act on", and therefore the one place
/// a remote destination is refused.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a policy and not a check at each call site.</b> There are two boundaries a destination can
/// reach the panel through — the endpoint that would store one, and the resolver that turns a stored
/// row into the destination an agent call names — and a refusal copied into both is a security
/// control that has to be fixed twice and stay fixed. The tree already carries that lesson written
/// down: five agent-error mappings were copied into five clients, agreed on the day they were
/// written, and became one file for exactly this reason (rules/csharp.md).
/// </para>
/// <para>
/// <b>Why the second call site is not defensive decoration.</b> The database can hold what a
/// validator refuses — a row written in <c>psql</c>, or by a future migration — and this module
/// already reasons that way in one place, where <c>BackupSchedule.MostRecentOccurrence</c> handles a
/// weekly schedule with no weekday because the column is nullable even though the validator refuses
/// the pair. A destination that reached the table any other way must be refused before it becomes an
/// argument to a call against a root process, not after.
/// </para>
/// <para>
/// <b>The refusal is a panel answer, not a relayed agent answer.</b> The agent refuses a remote
/// destination too (<c>remote_destination_refused</c>), and it is right to — it is root and it
/// re-validates everything. But relying on it would mean the panel asking the agent to do something
/// it knows it cannot, then translating a wire error into a message; the operator gets the same
/// sentence three hops later, and the panel has made a call it had no business making.
/// </para>
/// </remarks>
public static class RemoteDestinationPolicy
{
    /// <summary>Whether this build can act on a destination of the given kind.</summary>
    /// <param name="kind">The kind named by a request or held by a stored row.</param>
    /// <returns><c>true</c> for a kind every path can honour; <c>false</c> for one that is refused.</returns>
    /// <remarks>
    /// Written as an allow-list — the one kind that IS supported — rather than as a list of the
    /// refused ones. A kind added to the enum is then refused until somebody names it here, which is
    /// the direction a mistake should fall: a new destination kind that nothing can write to must not
    /// become writable by default the moment the member exists.
    /// </remarks>
    public static bool Admits(BackupDestinationKind kind)
    {
        return kind == BackupDestinationKind.Local;
    }
}
