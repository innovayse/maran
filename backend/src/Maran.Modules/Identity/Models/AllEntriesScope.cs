namespace Maran.Modules.Identity.Models;

/// <summary>
/// The administrator's scope: every entry of the journal, whichever account or reseller it concerns.
/// </summary>
public sealed record AllEntriesScope : AuditEventScope;
