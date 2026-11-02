using System.Diagnostics;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Identity.Extensions;

/// <summary>Reads the signed-in principal out of an <see cref="ICurrentUser"/> for code that only runs signed in.</summary>
public static class CurrentUserExtensions
{
    /// <summary>
    /// The signed-in principal of the current request: an administrator or a customer, with the
    /// operator viewing as them when there is one.
    /// </summary>
    /// <param name="currentUser">The current request's caller.</param>
    /// <returns>The principal.</returns>
    /// <exception cref="InvalidOperationException">
    /// The caller is anonymous. Every endpoint and handler that asks sits behind an
    /// <c>[Authorize]</c> policy, and the Host reads an authenticated request as a principal or
    /// refuses it, so reaching this anonymously is a wiring defect; it stops the request instead of
    /// acting as a user whose id is <see cref="Guid.Empty"/>.
    /// </exception>
    public static PrincipalCaller RequirePrincipal(this ICurrentUser currentUser)
    {
        return currentUser.Caller switch
        {
            PrincipalCaller principal => principal,
            AnonymousCaller => throw new InvalidOperationException(
                "This operation requires a signed-in caller, and the request is anonymous."),
            _ => throw new UnreachableException("Caller is sealed to SharedKernel: a principal or nobody."),
        };
    }
}
