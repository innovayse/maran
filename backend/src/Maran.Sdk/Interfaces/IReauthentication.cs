namespace Maran.Sdk.Interfaces;

/// <summary>
/// Asks whether a signed-in administrator can prove who they are again, right now, by typing their
/// current password. Used before an action that is as privileged as the panel gets (installing code
/// into the panel process, switching an outbound destination on), so a stolen session alone cannot do it.
/// </summary>
/// <remarks>
/// Identity answers it, because Identity owns the password and the lockout counter. A wrong password is
/// counted against the same lockout as a wrong sign-in, and the answer never says which part was wrong.
/// </remarks>
public interface IReauthentication
{
    /// <summary>Verifies a user's current password.</summary>
    /// <param name="userId">The signed-in user the password must belong to.</param>
    /// <param name="password">The password typed just now.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or a coded failure (wrong password, or the user is locked out).</returns>
    Task<Result> VerifyAsync(Guid userId, string password, CancellationToken ct);
}
