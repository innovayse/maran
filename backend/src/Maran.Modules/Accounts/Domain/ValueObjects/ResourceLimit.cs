using Maran.Modules.Accounts.Domain.Enums;

namespace Maran.Modules.Accounts.Domain.ValueObjects;

/// <summary>
/// What a plan says about one kind of resource: a cap with its maximum, or no cap at all.
/// </summary>
/// <remarks>
/// <para>
/// Built only through <see cref="Limited"/> and <see cref="Unlimited"/>. The cap is the whole state:
/// <see cref="Maximum"/> is a figure for a capped resource and <c>null</c> for an uncapped one, so
/// "unlimited with a maximum" cannot be spelled and no number stands in for the absence of a cap.
/// </para>
/// <para>
/// <b>Expand phase of the column change (rules/architecture.md).</b> The previous release stored a
/// kind column and a maximum column that held zero when uncapped. This release reads and writes the
/// nullable maximum column only, and ALSO keeps writing the two old columns through
/// <c>_legacyKind</c> and <c>_legacyMaximum</c>, which are derived from <see cref="Maximum"/> by the
/// one constructor and never read, so a panel rolled back to that release still finds every plan
/// ceiling where it looks. They are dropped in a later release, once no release reads them.
/// </para>
/// </remarks>
public sealed record ResourceLimit
{
    /// <summary>The old kind column's value, written beside the cap for the previous release; never read.</summary>
    private ResourceLimitKind _legacyKind;

    /// <summary>The old maximum column's value (zero when uncapped), written for the previous release; never read.</summary>
    private int _legacyMaximum;

    /// <summary>The cap, or <c>null</c> when the plan states none.</summary>
    public int? Maximum { get; }

    /// <summary>Whether the plan caps the resource; derived from <see cref="Maximum"/>, never stored.</summary>
    public ResourceLimitKind Kind
    {
        get { return Maximum is null ? ResourceLimitKind.Unlimited : ResourceLimitKind.Limited; }
    }

    /// <summary>Builds a limit; reached only through the two factories, and by EF Core on materialization.</summary>
    /// <param name="maximum">The cap, or <c>null</c> when uncapped.</param>
    private ResourceLimit(int? maximum)
    {
        Maximum = maximum;
        _legacyKind = maximum is null ? ResourceLimitKind.Unlimited : ResourceLimitKind.Limited;
        _legacyMaximum = maximum ?? 0;
    }

    /// <summary>A cap at <paramref name="maximum"/>.</summary>
    /// <param name="maximum">The most the account may own; never negative. Zero sells none of the resource.</param>
    /// <returns>The capped limit.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximum"/> is negative.</exception>
    public static ResourceLimit Limited(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        return new ResourceLimit(maximum);
    }

    /// <summary>No cap: the plan states nothing for this resource.</summary>
    /// <returns>The uncapped limit.</returns>
    public static ResourceLimit Unlimited()
    {
        return new ResourceLimit(maximum: null);
    }
}
