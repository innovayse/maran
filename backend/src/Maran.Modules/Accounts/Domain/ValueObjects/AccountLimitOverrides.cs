namespace Maran.Modules.Accounts.Domain.ValueObjects;

/// <summary>
/// The figures an account holds in place of its plan's: the inode, process and open-file ceilings a
/// creator chose for this one account (issue #194). A figure that is <c>null</c> is not stated, and
/// the account follows its plan for it, so the effective value of every ceiling is
/// <c>override ?? plan</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>"Not stated" is the only way to say "follow the plan", and a stated figure is always positive.</b>
/// The wire lets a creator send zero for the inode and open-file ceilings to mean "no limit"; that is
/// not stored. A plan states no inode or open-file figure at all, so for those two "follow the plan"
/// IS "no limit", and a stored zero would be a second spelling of the same thing. The process ceiling
/// has no zero on the wire: a plan states one, and an account cannot be asked to run no process.
/// </para>
/// <para>
/// <b>Why a value and not three loose columns on the account.</b> The three are pushed to the host
/// together with the plan's own figures every time the plan changes, and a plan change that forgot
/// them would silently erase the choice from the host. <see cref="ApplyTo"/> is the one place the
/// process override meets the plan's runtime ceilings, so that rule is stated once.
/// </para>
/// </remarks>
public sealed record AccountLimitOverrides
{
    /// <summary>The most files and directories the account may own, or <c>null</c> when it follows the plan.</summary>
    public long? InodeLimit { get; private set; }

    /// <summary>The most processes and threads the account may run at once, or <c>null</c> when it follows the plan.</summary>
    public int? ProcessLimit { get; private set; }

    /// <summary>The most files one login session of the account may hold open, or <c>null</c> when it follows the plan.</summary>
    public int? OpenFilesLimit { get; private set; }

    /// <summary>Whether the account follows its plan for every ceiling.</summary>
    public bool IsEmpty
    {
        get { return InodeLimit is null && ProcessLimit is null && OpenFilesLimit is null; }
    }

    /// <summary>Builds the overrides; reached only through <see cref="Of"/> and <see cref="None"/>.</summary>
    /// <param name="inodeLimit">The inode ceiling, or <c>null</c>.</param>
    /// <param name="processLimit">The process ceiling, or <c>null</c>.</param>
    /// <param name="openFilesLimit">The open-files ceiling, or <c>null</c>.</param>
    private AccountLimitOverrides(long? inodeLimit, int? processLimit, int? openFilesLimit)
    {
        InodeLimit = inodeLimit;
        ProcessLimit = processLimit;
        OpenFilesLimit = openFilesLimit;
    }

    /// <summary>Parameterless constructor required by EF Core materialization of the owned value.</summary>
    private AccountLimitOverrides()
    {
    }

    /// <summary>States the overrides.</summary>
    /// <param name="inodeLimit">The inode ceiling, or <c>null</c> to follow the plan.</param>
    /// <param name="processLimit">The process ceiling, or <c>null</c> to follow the plan.</param>
    /// <param name="openFilesLimit">The open-files ceiling, or <c>null</c> to follow the plan.</param>
    /// <returns>The overrides.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a stated figure is not positive.</exception>
    public static AccountLimitOverrides Of(long? inodeLimit, int? processLimit, int? openFilesLimit)
    {
        GuardPositive(inodeLimit, nameof(inodeLimit));
        GuardPositive(processLimit, nameof(processLimit));
        GuardPositive(openFilesLimit, nameof(openFilesLimit));

        return new AccountLimitOverrides(inodeLimit, processLimit, openFilesLimit);
    }

    /// <summary>No override: the account follows its plan for every ceiling.</summary>
    /// <returns>Empty overrides, a new instance each time so no two accounts share one owned value.</returns>
    public static AccountLimitOverrides None()
    {
        return new AccountLimitOverrides();
    }

    /// <summary>
    /// The runtime ceilings the account is held to: the plan's, except the process ceiling when this
    /// account states one.
    /// </summary>
    /// <param name="plan">The ceilings of the account's plan.</param>
    /// <returns>The ceilings to put on the account's slice.</returns>
    public RuntimeLimits ApplyTo(RuntimeLimits plan)
    {
        var processes = ProcessLimit is { } stated ? ResourceLimit.Limited(stated) : plan.MaxProcesses;

        return RuntimeLimits.Of(plan.CpuQuotaPercent, plan.MemoryMaxMb, processes);
    }

    /// <summary>Refuses a stated figure that is not positive.</summary>
    /// <param name="figure">The figure to check.</param>
    /// <param name="name">The parameter it arrived as, for the exception.</param>
    /// <typeparam name="T">The figure's numeric type.</typeparam>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="figure"/> is stated and not positive.</exception>
    private static void GuardPositive<T>(T? figure, string name)
        where T : struct, IComparable<T>
    {
        if (figure is { } stated && stated.CompareTo(default) <= 0)
        {
            throw new ArgumentOutOfRangeException(name, stated, "A stated override must be positive.");
        }
    }
}
