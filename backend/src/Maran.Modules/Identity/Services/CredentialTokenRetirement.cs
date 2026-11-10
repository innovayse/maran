using Maran.Modules.Identity.Persistence;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Retires every outstanding invitation token and password-reset token of one login: the single
/// answer to "what must stop working when the thing a token was mailed for changes".
/// </summary>
/// <remarks>
/// <para>
/// A token is permission to set the login's password, issued to the address the login had when it
/// was mailed. Once that address or the password itself changes, the mail is in the hands of
/// somebody the change was meant to shut out — a typo, a former contact, a mailbox read by a third
/// party — and the token must not outlive the change. Every credential-changing path calls this
/// type, so a path added later has one call to make and no loop to forget.
/// </para>
/// <para>
/// <b>It does not save.</b> The caller saves the retirement in the same <c>SaveChanges</c> as the
/// change itself, so there is no instant at which the new address or password is stored and the old
/// token is still live (or the reverse).
/// </para>
/// <para>
/// <b>Rows are stamped as spent, not deleted</b>, as every other retirement here is, so a later
/// presentation of the token reads as a refused replay in the journal and not as an unknown token.
/// </para>
/// </remarks>
public sealed class CredentialTokenRetirement
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the service.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public CredentialTokenRetirement(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Marks every unspent invitation and reset token of the login as spent, without saving.</summary>
    /// <param name="userId">The login whose outstanding tokens are retired.</param>
    /// <param name="at">The instant of retirement, taken from <see cref="IClock"/>.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>How many tokens were retired; zero when the login had none outstanding.</returns>
    public async Task<int> RetireAsync(Guid userId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var resets = await _dbContext.PasswordResetTokens
            .Where(token => token.UserId == userId && token.UsedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var reset in resets)
        {
            reset.Consume(at);
        }

        var invitations = await _dbContext.InvitationTokens
            .Where(token => token.UserId == userId && token.UsedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var invitation in invitations)
        {
            invitation.Consume(at);
        }

        return resets.Count + invitations.Count;
    }
}
