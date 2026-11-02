namespace Maran.SharedKernel.Callers;

/// <summary>
/// No principal at all: an unauthenticated request, a background message, a design-time context.
/// </summary>
/// <remarks>
/// It carries nothing, because there is nothing to carry. An anonymous caller is not an administrator
/// and owns no account, so a tenant query filter that is not bypassed returns nothing for it.
/// </remarks>
public sealed record AnonymousCaller : Caller
{
    /// <summary>The one anonymous caller; it has no state, so there is no reason to make another.</summary>
    public static readonly AnonymousCaller Instance = new();

    /// <summary>Creates the anonymous caller; use <see cref="Instance"/>.</summary>
    private AnonymousCaller()
    {
    }
}
