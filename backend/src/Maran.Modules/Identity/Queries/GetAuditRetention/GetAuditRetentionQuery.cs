namespace Maran.Modules.Identity.Queries.GetAuditRetention;

/// <summary>
/// Reads how long the audit journal is kept. It takes no parameters: there is exactly one retention
/// setting on a panel, and it is not scoped to an account.
/// </summary>
public sealed record GetAuditRetentionQuery();
