using Maran.Agent.Client.Interfaces;
using Maran.Sdk.Events;

namespace Maran.Modules.Sftp.IntegrationEvents.Handlers;

/// <summary>
/// Unlocks every SFTP login of an account that is about to be reactivated
/// (<see cref="AccountResuming"/>), giving the customer back the credential they already had.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same credential, not a new one.</b> The suspension prefixed each login's stored hash and
/// changed nothing else, so removing the prefix restores exactly the password the customer was
/// shown when the login was created. A resume that reset passwords would leave every customer of a
/// reactivated account locked out of their own files with no way to find out why.
/// </para>
/// <para>
/// <b>One honest limit, and it is reported rather than hidden.</b> A login that has no password at
/// all cannot be unlocked — <c>usermod --unlock</c> refuses that while still exiting zero — so such
/// a login stays locked after the resume. Every login the agent creates is given a password in the
/// same operation that creates it, so this can only be a login somebody made by hand; the
/// suspension state reports it as still locked and the Accounts handler refuses the reactivation
/// rather than reporting a restoration it did not observe.
/// </para>
/// <para>
/// <b>The failure is not swallowed</b>, for the reason its sibling gives: anything thrown here
/// aborts the reactivation and leaves the account suspended, which is recoverable.
/// </para>
/// </remarks>
public sealed class AccountResumingHandler
{
    /// <summary>The agent, which owns the host's password database.</summary>
    private readonly IAgentSftpClient _agent;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that unlocks the account's logins.</param>
    public AccountResumingHandler(IAgentSftpClient agent)
    {
        _agent = agent;
    }

    /// <summary>Unlocks every SFTP login the host holds for the account.</summary>
    /// <param name="message">The account about to be reactivated.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="InvalidOperationException">
    /// The agent refused. Thrown rather than returned, for the reason its sibling gives.
    /// </exception>
    public async Task HandleAsync(AccountResuming message, CancellationToken cancellationToken)
    {
        var unlocked = await _agent.SetAccountLoginsLockedAsync(message.Username, false, cancellationToken);
        if (!unlocked.IsSuccess)
        {
            throw new InvalidOperationException(
                $"the sftp logins of {message.Username} could not be unlocked: {unlocked.Error?.Code}");
        }
    }
}
