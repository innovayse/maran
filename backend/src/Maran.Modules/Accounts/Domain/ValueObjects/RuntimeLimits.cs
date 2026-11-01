namespace Maran.Modules.Accounts.Domain.ValueObjects;

/// <summary>
/// What a plan says about the CPU, memory and processes an account may use at once: three ceilings
/// the agent writes onto the account's systemd slice (issue #133).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these three, and why together.</b> The count limits and the disk quota bound what an
/// account may OWN; nothing bounded what it may RUN, so one tenant's busy loop or fork storm could
/// starve every other tenant on the server. The three are written to the host in one call and
/// replaced whole, so an account never holds one plan's CPU quota beside another plan's memory
/// ceiling — which is why they are one value rather than three loose fields on the plan.
/// </para>
/// <para>
/// <b>"No ceiling" is a case, never a number.</b> Each is a <see cref="ResourceLimit"/>, whose
/// <see cref="Enums.ResourceLimitKind.Unlimited"/> is stated on purpose; a limited one must be
/// positive, because a ceiling of zero is not a smaller allowance but an account that cannot run at
/// all. A plan that existed before these ceilings did reads as unlimited on all three, which is
/// exactly what the host enforced for it.
/// </para>
/// </remarks>
public sealed record RuntimeLimits
{
    /// <summary>The share of CPU time the account may use, in percent of ONE CPU (200 is two whole cores).</summary>
    public ResourceLimit CpuQuotaPercent { get; private set; }

    /// <summary>The most memory the account's processes may hold together, in megabytes (MiB).</summary>
    public ResourceLimit MemoryMaxMb { get; private set; }

    /// <summary>The most processes and threads the account may run at once.</summary>
    public ResourceLimit MaxProcesses { get; private set; }

    /// <summary>Builds the ceilings; reached only through <see cref="Of"/> and <see cref="Unlimited"/>.</summary>
    /// <param name="cpuQuotaPercent">The CPU ceiling.</param>
    /// <param name="memoryMaxMb">The memory ceiling.</param>
    /// <param name="maxProcesses">The process ceiling.</param>
    private RuntimeLimits(ResourceLimit cpuQuotaPercent, ResourceLimit memoryMaxMb, ResourceLimit maxProcesses)
    {
        CpuQuotaPercent = cpuQuotaPercent;
        MemoryMaxMb = memoryMaxMb;
        MaxProcesses = maxProcesses;
    }

    /// <summary>Parameterless constructor required by EF Core materialization of the owned value.</summary>
    private RuntimeLimits()
    {
        CpuQuotaPercent = ResourceLimit.Unlimited();
        MemoryMaxMb = ResourceLimit.Unlimited();
        MaxProcesses = ResourceLimit.Unlimited();
    }

    /// <summary>States the three ceilings.</summary>
    /// <param name="cpuQuotaPercent">The CPU ceiling, in percent of one CPU.</param>
    /// <param name="memoryMaxMb">The memory ceiling, in megabytes.</param>
    /// <param name="maxProcesses">The process ceiling.</param>
    /// <returns>The ceilings.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a limited ceiling is zero.</exception>
    public static RuntimeLimits Of(ResourceLimit cpuQuotaPercent, ResourceLimit memoryMaxMb, ResourceLimit maxProcesses)
    {
        GuardPositive(cpuQuotaPercent, nameof(cpuQuotaPercent));
        GuardPositive(memoryMaxMb, nameof(memoryMaxMb));
        GuardPositive(maxProcesses, nameof(maxProcesses));

        // Each ceiling is copied: EF Core persists them as owned values, and one instance reachable
        // through two of them (a caller passing the same Unlimited() three times) would be refused
        // at the save as an owned entity shared between two owners.
        return new RuntimeLimits(cpuQuotaPercent with { }, memoryMaxMb with { }, maxProcesses with { });
    }

    /// <summary>No ceiling on any of the three: what the host enforced for every plan before them.</summary>
    /// <returns>The uncapped ceilings, each its own instance.</returns>
    public static RuntimeLimits Unlimited()
    {
        return new RuntimeLimits();
    }

    /// <summary>Refuses a limited ceiling of zero.</summary>
    /// <param name="limit">The ceiling to check.</param>
    /// <param name="name">The parameter it arrived as, for the exception.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit"/> is limited at zero.</exception>
    private static void GuardPositive(ResourceLimit limit, string name)
    {
        if (limit.Kind == Enums.ResourceLimitKind.Limited && limit.Maximum == 0)
        {
            throw new ArgumentOutOfRangeException(name, limit.Maximum, "A limited runtime ceiling must be positive.");
        }
    }
}
