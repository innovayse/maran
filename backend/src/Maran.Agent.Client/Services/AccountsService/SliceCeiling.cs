namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>
/// One ceiling of an account's systemd slice as the agent's <c>SetAccountResourceLimits</c> takes it:
/// a positive figure, or no ceiling at all.
/// </summary>
/// <remarks>
/// Built only through <see cref="Limited"/> and <see cref="Unlimited"/>, so a limited ceiling is never
/// zero — the agent refuses zero, because an absent wire figure decodes to it — and an unlimited one
/// never carries a figure a caller could mistake for a cap. <see cref="Maximum"/> is zero when
/// unlimited; a caller reads <see cref="Kind"/> first, always.
/// </remarks>
public sealed record SliceCeiling
{
    /// <summary>Whether the slice is capped for this resource; decides whether <see cref="Maximum"/> means anything.</summary>
    public SliceCeilingKind Kind { get; }

    /// <summary>The cap when <see cref="Kind"/> is <see cref="SliceCeilingKind.Limited"/>; zero otherwise.</summary>
    public uint Maximum { get; }

    /// <summary>Builds a ceiling; reached only through the two factories.</summary>
    /// <param name="kind">Whether the resource is capped.</param>
    /// <param name="maximum">The cap, or zero when uncapped.</param>
    private SliceCeiling(SliceCeilingKind kind, uint maximum)
    {
        Kind = kind;
        Maximum = maximum;
    }

    /// <summary>A cap at <paramref name="maximum"/>, in the unit the field it is sent in names.</summary>
    /// <param name="maximum">The cap; never zero.</param>
    /// <returns>The capped ceiling.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximum"/> is zero.</exception>
    public static SliceCeiling Limited(uint maximum)
    {
        ArgumentOutOfRangeException.ThrowIfZero(maximum);
        return new SliceCeiling(SliceCeilingKind.Limited, maximum);
    }

    /// <summary>No ceiling for this resource.</summary>
    /// <returns>The uncapped ceiling.</returns>
    public static SliceCeiling Unlimited()
    {
        return new SliceCeiling(SliceCeilingKind.Unlimited, 0);
    }
}
