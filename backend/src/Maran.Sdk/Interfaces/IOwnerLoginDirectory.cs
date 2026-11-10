using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// Answers whether the login a new account's owner is to receive can be made at all: a read-only window
/// implemented by the module that owns the logins (Identity), so the module that creates the account can
/// refuse a name or an e-mail address another login already holds BEFORE it provisions anything, and
/// without a second definition of what makes two logins the same
/// (rules/architecture.md "A shared facility's contract lives in Maran.Sdk").
/// </summary>
/// <remarks>
/// It discloses only which of the two values clashes, never whose login holds it. The answer is the
/// state at the moment of the question; the unique indexes on the logins stay the guarantee, and a
/// clash that appears between this question and the insert still fails the insert.
/// </remarks>
public interface IOwnerLoginDirectory
{
    /// <summary>Finds what stops a login with this name and e-mail address from being made.</summary>
    /// <param name="username">The login name the owner would take.</param>
    /// <param name="email">The e-mail address the owner would take.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// <see cref="OwnerLoginConflict.None"/> when both are free; otherwise the first value another login
    /// holds, the name before the address.
    /// </returns>
    Task<OwnerLoginConflict> FindConflictAsync(string username, string email, CancellationToken cancellationToken);
}
