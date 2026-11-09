using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Domain.Policies;

/// <summary>
/// The one place the arithmetic of a reseller's envelope is written: whether a plan fits under the
/// ceilings, and whether one more plan, one more account or a larger disk allocation fits under the
/// counts. Pure: it reads figures and answers, so a boundary value is a unit test and not a database
/// session.
/// </summary>
/// <remarks>
/// Every comparison is inclusive: a figure equal to its limit is accepted, one over it is refused.
/// Lowering an envelope below what is already owned is allowed (existing plans and accounts keep
/// working); this policy only judges NEW writes, and the caller passes the figures
/// as they would stand after the write.
/// </remarks>
public static class ResellerEnvelopePolicy
{
    /// <summary>Names every figure of <paramref name="limits"/> that breaks a ceiling of <paramref name="envelope"/>.</summary>
    /// <param name="envelope">What the reseller was sold.</param>
    /// <param name="limits">The figures of the plan being created or edited.</param>
    /// <returns>
    /// The offending figures by the name the plan gives them (for example <c>DiskQuotaMb</c>,
    /// <c>Features</c>, <c>CpuQuotaPercent</c>), in a stable order; empty when the plan fits.
    /// </returns>
    public static IReadOnlyList<string> Violations(ResellerEnvelope envelope, PlanLimits limits)
    {
        var violations = new List<string>();

        AddIfOver(violations, nameof(PlanLimits.DiskQuotaMb), limits.DiskQuotaMb, envelope.MaxDiskQuotaMb);
        AddIfOver(violations, nameof(PlanLimits.MaxSites), limits.MaxSites, envelope.MaxSites);
        AddIfOver(violations, nameof(PlanLimits.MaxDatabases), limits.MaxDatabases, envelope.MaxDatabases);
        AddIfOver(violations, nameof(PlanLimits.MaxSftpUsers), limits.MaxSftpUsers, envelope.MaxSftpUsers);
        AddIfOver(violations, nameof(PlanLimits.MaxFtpUsers), limits.MaxFtpUsers, envelope.MaxFtpUsers);
        AddIfOver(violations, nameof(PlanLimits.MaxCronEntries), limits.MaxCronEntries, envelope.MaxCronEntries);
        AddIfOver(
            violations,
            nameof(PlanLimits.MaxPhpWorkersPerPool),
            limits.MaxPhpWorkersPerPool,
            envelope.MaxPhpWorkersPerPool);
        AddIfOver(violations, nameof(PlanLimits.MaxMailboxes), limits.MaxMailboxes, envelope.MaxMailboxes);
        AddIfOver(
            violations,
            nameof(PlanLimits.MailboxQuotaPoolMib),
            limits.MailboxQuotaPoolMib,
            envelope.MailboxQuotaPoolMib);

        if ((limits.Features & ~envelope.Features) != AccountFeature.None)
        {
            violations.Add(nameof(PlanLimits.Features));
        }

        AddIfRuntimeOver(
            violations,
            nameof(RuntimeLimits.CpuQuotaPercent),
            limits.RuntimeLimits.CpuQuotaPercent,
            envelope.RuntimeLimits.CpuQuotaPercent);
        AddIfRuntimeOver(
            violations,
            nameof(RuntimeLimits.MemoryMaxMb),
            limits.RuntimeLimits.MemoryMaxMb,
            envelope.RuntimeLimits.MemoryMaxMb);
        AddIfRuntimeOver(
            violations,
            nameof(RuntimeLimits.MaxProcesses),
            limits.RuntimeLimits.MaxProcesses,
            envelope.RuntimeLimits.MaxProcesses);

        return violations;
    }

    /// <summary>Whether the reseller may own one more plan.</summary>
    /// <param name="envelope">What the reseller was sold.</param>
    /// <param name="plansOwned">How many plans the reseller owns now.</param>
    /// <returns><c>true</c> when <paramref name="plansOwned"/> plus one is within <see cref="ResellerEnvelope.MaxPlans"/>.</returns>
    public static bool PlanAllowed(ResellerEnvelope envelope, int plansOwned)
    {
        return plansOwned < envelope.MaxPlans;
    }

    /// <summary>Whether the reseller may own one more account.</summary>
    /// <param name="envelope">What the reseller was sold.</param>
    /// <param name="accountsOwned">How many accounts, in any status, the reseller owns now.</param>
    /// <returns><c>true</c> when <paramref name="accountsOwned"/> plus one is within <see cref="ResellerEnvelope.MaxAccounts"/>.</returns>
    public static bool AccountAllowed(ResellerEnvelope envelope, int accountsOwned)
    {
        return accountsOwned < envelope.MaxAccounts;
    }

    /// <summary>Whether a disk allocation fits the reseller's pool.</summary>
    /// <param name="envelope">What the reseller was sold.</param>
    /// <param name="allocatedDiskMb">
    /// The total the reseller would have promised AFTER the write: the sum of the plan disk quota of
    /// every account it would own, counted with the new plan or the new account in place.
    /// </param>
    /// <returns><c>true</c> when the total is within <see cref="ResellerEnvelope.DiskPoolMb"/>.</returns>
    public static bool DiskPoolAllowed(ResellerEnvelope envelope, long allocatedDiskMb)
    {
        return allocatedDiskMb <= envelope.DiskPoolMb;
    }

    /// <summary>Adds <paramref name="name"/> when a plan figure is over its ceiling.</summary>
    /// <param name="violations">The list being built.</param>
    /// <param name="name">The figure's name.</param>
    /// <param name="value">The plan's figure.</param>
    /// <param name="ceiling">The envelope's ceiling for it.</param>
    private static void AddIfOver(List<string> violations, string name, int value, int ceiling)
    {
        if (value > ceiling)
        {
            violations.Add(name);
        }
    }

    /// <summary>Adds <paramref name="name"/> when a plan's runtime figure is over the envelope's ceiling for it.</summary>
    /// <param name="violations">The list being built.</param>
    /// <param name="name">The ceiling's name.</param>
    /// <param name="requested">What the plan states: a cap, or none.</param>
    /// <param name="ceiling">What the envelope allows: a cap, or none.</param>
    private static void AddIfRuntimeOver(
        List<string> violations,
        string name,
        ResourceLimit requested,
        ResourceLimit ceiling)
    {
        if (ceiling.Maximum is null)
        {
            return;
        }

        // A plan that states no cap is unbounded, which is over any ceiling that exists.
        if (requested.Maximum is null || requested.Maximum > ceiling.Maximum)
        {
            violations.Add(name);
        }
    }
}
