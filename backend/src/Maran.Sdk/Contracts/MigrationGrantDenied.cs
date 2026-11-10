namespace Maran.Sdk.Contracts;

/// <summary>The grant was refused, for a reason the caller is never told.</summary>
public sealed record MigrationGrantDenied : MigrationGrantAuthentication
{
    /// <summary>The one denial; it has no state, so there is no reason to make another.</summary>
    public static readonly MigrationGrantDenied Instance = new();

    /// <summary>Creates the denial; use <see cref="Instance"/>.</summary>
    private MigrationGrantDenied()
    {
    }
}
