using Maran.Modules.Accounts.Domain.Enums;

namespace Maran.Modules.Accounts.Domain.ValueObjects;

/// <summary>
/// What a plan says about one kind of resource: a cap with its maximum, or no cap at all.
/// </summary>
/// <remarks>
/// Built only through <see cref="Limited"/> and <see cref="Unlimited"/>, so a cap is never negative
/// and an uncapped resource never carries a maximum a caller could mistake for one.
/// <see cref="Maximum"/> is zero when uncapped; a caller reads <see cref="Kind"/> first, always.
/// </remarks>
public sealed record ResourceLimit
{
    /// <summary>Whether the plan caps the resource; decides whether <see cref="Maximum"/> means anything.</summary>
    public ResourceLimitKind Kind { get; }

    /// <summary>The cap when <see cref="Kind"/> is <see cref="ResourceLimitKind.Limited"/>; zero otherwise.</summary>
    public int Maximum { get; }

    /// <summary>Builds a limit; reached only through the two factories.</summary>
    /// <param name="kind">Whether the resource is capped.</param>
    /// <param name="maximum">The cap, or zero when uncapped.</param>
    private ResourceLimit(ResourceLimitKind kind, int maximum)
    {
        Kind = kind;
        Maximum = maximum;
    }

    /// <summary>A cap at <paramref name="maximum"/>.</summary>
    /// <param name="maximum">The most the account may own; never negative.</param>
    /// <returns>The capped limit.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximum"/> is negative.</exception>
    public static ResourceLimit Limited(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        return new ResourceLimit(ResourceLimitKind.Limited, maximum);
    }

    /// <summary>No cap: the plan states nothing for this resource.</summary>
    /// <returns>The uncapped limit.</returns>
    public static ResourceLimit Unlimited()
    {
        return new ResourceLimit(ResourceLimitKind.Unlimited, 0);
    }
}
