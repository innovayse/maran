namespace Maran.Modules.Identity.Queries.GetAuditChainStatus;

/// <summary>
/// Reads how far the audit journal has been sealed into the tamper-evidence chain. It takes no
/// parameters: there is one journal and one chain on a panel.
/// </summary>
public sealed record GetAuditChainStatusQuery();
