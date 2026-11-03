using Maran.Modules.Accounts.Models;

namespace Maran.Modules.Accounts.Mappers;

/// <summary>
/// Restates a <see cref="ResellerRefusal"/> as the failed result of whichever operation was refused,
/// so the four handlers that ask the reseller rules answer a refusal the same way.
/// </summary>
/// <remarks>
/// It translates and never decides (rules/csharp.md "A mapper translates; it never decides"): whether
/// the refusal carries facts was settled by the guard that made it, and is only carried across here.
/// </remarks>
public static class ResellerRefusalMapper
{
    /// <summary>Wraps the refusal as a failed result.</summary>
    /// <typeparam name="T">The success type of the refused operation.</typeparam>
    /// <param name="refusal">The refusal to answer with.</param>
    /// <returns>A failed result carrying the refusal's error and, when it has any, its facts.</returns>
    public static Result<T> ToFailure<T>(ResellerRefusal refusal)
    {
        return refusal.Extension is null
            ? Result<T>.Fail(refusal.Error)
            : Result<T>.Fail(refusal.Error, refusal.Extension);
    }
}
