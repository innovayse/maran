namespace Maran.Sdk.Contracts;

/// <summary>What every importer may need to know about the whole package while it judges its own section.</summary>
/// <param name="AccountName">The account's system user name; the target keeps it (decision D1).</param>
/// <param name="PlanId">The plan the administrator chose for the account on this server.</param>
/// <param name="ResourceCounts">How many resources of each kind the package holds, keyed by the module id that owns the kind.</param>
/// <param name="HomeBytes">The size of the account's home directory in the package.</param>
public sealed record MigrationPlanContext(
    string AccountName,
    Guid PlanId,
    IReadOnlyDictionary<string, int> ResourceCounts,
    long HomeBytes);
