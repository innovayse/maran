using System.Diagnostics;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Identity.Domain.Policies;

/// <summary>The rule that an operation only runs for a signed-in caller, and the principal it then acts as.</summary>
public static class SignedInCallerPolicy
{
    /// <summary>
    /// The signed-in principal of the current request: an administrator, a reseller or a customer,
    /// with the operator viewing as them when there is one.
    /// </summary>
    /// <param name="currentUser">The current request's caller.</param>
    /// <returns>The principal.</returns>
    /// <exception cref="InvalidOperationException">
    /// The caller is anonymous. Every endpoint and handler that asks sits behind an
    /// <c>[Authorize]</c> policy, and the Host reads an authenticated request as a principal or
    /// refuses it, so reaching this anonymously is a wiring defect; it stops the request instead of
    /// acting as a user whose id is <see cref="Guid.Empty"/>.
    /// </exception>
    public static PrincipalCaller Require(ICurrentUser currentUser)
    {
        // Every case is named so that a sixth Caller subtype is a deliberate edit here, not a silent fall-through.
        return currentUser.Caller switch
        {
            AdministratorCaller administrator => administrator,
            CustomerCaller customer => customer,
            AccountlessCustomerCaller accountless => accountless,
            ResellerCaller reseller => reseller,
            AnonymousCaller => throw new InvalidOperationException(
                "This operation requires a signed-in caller, and the request is anonymous."),
            _ => throw new UnreachableException("Caller is sealed to SharedKernel: one of its five cases."),
        };
    }
}
