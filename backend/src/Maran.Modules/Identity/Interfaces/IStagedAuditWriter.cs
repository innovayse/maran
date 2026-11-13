using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Interfaces;

/// <summary>
/// Adds an entry to the audit journal's table without saving it, so the entry commits in the same save as
/// the change it records.
/// </summary>
/// <remarks>
/// Identity owns the journal's table, so its own services can join the unit of work of the context they
/// already hold. <see cref="Maran.Sdk.Interfaces.IAuditWriter"/> is the contract every other module uses; it
/// saves on every call and cannot make an entry and a change one commit.
/// </remarks>
public interface IStagedAuditWriter
{
    /// <summary>Tracks one entry on the module's context; the caller's save stores it.</summary>
    /// <param name="entry">What happened.</param>
    void Stage(AuditEntry entry);
}
