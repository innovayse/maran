using Maran.Modules.Accounts.Common;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Interfaces;

/// <summary>
/// What an administrator states when defining or editing a plan: its name and every limit it sells.
/// </summary>
/// <remarks>
/// Creating a plan and editing one take exactly the same fields, and the rules about what a field may
/// hold are the same too, so both commands implement this and share one validator over it
/// (<see cref="Validators.PlanDefinitionValidator{TCommand}"/>) instead of two copies that could come
/// to disagree about what a sellable plan is.
/// </remarks>
public interface IPlanDefinition
{
    /// <summary>The plan's name, as an operator wants it read.</summary>
    string Name { get; }

    /// <summary>The account's disk quota, in megabytes.</summary>
    int DiskQuotaMb { get; }

    /// <summary>The maximum number of sites the account may create.</summary>
    int MaxSites { get; }

    /// <summary>The maximum number of databases the account may create.</summary>
    int MaxDatabases { get; }

    /// <summary>The maximum number of SFTP logins the account may create.</summary>
    int MaxSftpUsers { get; }

    /// <summary>The maximum number of FTPS logins the account may create.</summary>
    int MaxFtpUsers { get; }

    /// <summary>The maximum number of cron entries the account may keep.</summary>
    int MaxCronEntries { get; }

    /// <summary>The maximum number of php-fpm workers one of the account's pools may run.</summary>
    int MaxPhpWorkersPerPool { get; }

    /// <summary>The share of CPU time the account may use, in percent of ONE CPU, or no ceiling.</summary>
    ResourceCeilingDto CpuQuotaPercent { get; }

    /// <summary>The most memory the account's processes may hold together, in megabytes, or no ceiling.</summary>
    ResourceCeilingDto MemoryMaxMb { get; }

    /// <summary>The most processes and threads the account may run at once, or no ceiling.</summary>
    ResourceCeilingDto MaxProcesses { get; }

    /// <summary>
    /// The features the plan sells, always stated in full: creating a plan sells exactly these, and
    /// editing one replaces its set with these. An empty list sells none. There is no "not stated"
    /// reading; a request that leaves the list out is refused.
    /// </summary>
    IReadOnlyList<AccountFeature> Features { get; }

    /// <summary>
    /// The largest PHP settings a customer on the plan may choose (issue #138), or <c>null</c> when the
    /// request does not state them: creating a plan then sells the conservative default, and editing
    /// one leaves its current bounds as they are. Additive API evolution, so a client that predates the
    /// bounds neither loses nor invents any.
    /// </summary>
    PhpLimitsDto? PhpLimits { get; }

    /// <summary>
    /// What an account on the plan may do with mail (issue #177), or <c>null</c> when the request does
    /// not state it: creating a plan then sells <c>MailLimits.Default</c>, and editing one leaves its
    /// current figures as they are. Additive API evolution, so a client that predates mail neither
    /// loses nor invents any.
    /// </summary>
    MailLimitsDto? MailLimits { get; }

    /// <summary>
    /// The most DNS zones an account on the plan may own (issue #178), or <c>null</c> when the request does
    /// not state it: creating a plan then sells <c>Plan.DefaultMaxDnsZones</c>, and editing one leaves its
    /// current allowance as it is. Additive API evolution, so a client that predates DNS neither loses nor
    /// invents one. A plan a reseller owns cannot state it: the reseller envelope does not bound it yet,
    /// so such a plan always holds the default.
    /// </summary>
    int? MaxDnsZones { get; }
}
