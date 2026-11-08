namespace Maran.Modules.Identity.Models;

/// <summary>
/// Which entries of the audit journal the caller may read. A closed set of two cases, so a reader of
/// the journal cannot forget to ask: it receives a scope from <c>AuditEventScopeReader</c> and hands
/// it to <c>AuditEventQuery</c>, which narrows the journal to it.
/// </summary>
/// <remarks>
/// The cases are <see cref="AllEntriesScope"/> (an administrator: every entry on the server) and
/// <see cref="ResellerEntriesScope"/> (a reseller: the entries about its own accounts and its own
/// login). Every other caller has no scope at all — the reader refuses it, which is why there is no
/// "nothing" case here that a careless caller could treat as "everything".
/// </remarks>
public abstract record AuditEventScope;
