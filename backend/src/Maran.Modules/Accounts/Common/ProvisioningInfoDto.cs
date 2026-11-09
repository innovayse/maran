namespace Maran.Modules.Accounts.Common;

/// <summary>
/// What an integration reads to know what it may provision: the revision of the provisioning API it is
/// talking to, and the reseller's envelope beside what the reseller has used of it. Nothing about the
/// host: no panel version, and no disk or capacity figure of the server, which a reseller has no
/// business reading.
/// </summary>
/// <param name="ApiRevision">The revision of the provisioning API; it grows only by additions within a major version.</param>
/// <param name="AccountCount">How many accounts, in any status, the reseller owns right now.</param>
/// <param name="PlanCount">How many plans the reseller owns right now.</param>
/// <param name="DiskAllocatedMb">The disk, in megabytes, promised to the accounts the reseller owns.</param>
/// <param name="Envelope">What the reseller was sold; it may sit below the usage when it was lowered.</param>
public sealed record ProvisioningInfoDto(
    int ApiRevision,
    int AccountCount,
    int PlanCount,
    long DiskAllocatedMb,
    ResellerEnvelopeDto Envelope);
