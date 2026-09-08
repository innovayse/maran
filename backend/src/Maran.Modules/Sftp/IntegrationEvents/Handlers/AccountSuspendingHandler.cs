using Maran.Agent.Client.Interfaces;
using Maran.Sdk.Events;

namespace Maran.Modules.Sftp.IntegrationEvents.Handlers;

/// <summary>
/// Locks every SFTP login of an account that is about to be suspended
/// (<see cref="AccountSuspending"/>), so a suspended customer stops being able to write to their
/// own files.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists, and why it is the sharpest half of the whole cascade.</b> An SFTP login is
/// its OWN passwd entry — <c>&lt;account&gt;_&lt;name&gt;</c>, created with
/// <c>useradd --non-unique --uid &lt;account uid&gt;</c> so that it writes as the account — so the
/// <c>usermod --lock &lt;account&gt;</c> a suspension performs reaches none of them. That is not
/// "a website keeps serving": it is a live WRITE credential into the customer's home surviving the
/// suspension that was supposed to take it away. Measured against a real OpenSSH daemon before this
/// handler existed: the same login that authenticated before the suspension authenticated after it.
/// </para>
/// <para>
/// <b>It writes no rows and reads none.</b> The agent enumerates the account's logins from the
/// HOST's password database, not from this module's table, for the reason the deletion cascade
/// gives: a table can only describe what the panel remembers creating, and a login it has forgotten
/// is exactly the one that would keep working. So this handler is one call and no query.
/// </para>
/// <para>
/// <b>Nothing is deleted and no password is reset.</b> Locking prefixes the stored hash; the resume
/// removes that prefix and gives the customer back the credential they already have, rather than a
/// new one they would have to be told about. The jail, the bind mount and the files behind the
/// logins are untouched — suspension revokes a key, it does not destroy what the key opened.
/// </para>
/// <para>
/// <b>The failure is not swallowed.</b> Anything thrown here propagates to the Accounts handler,
/// which abandons the suspension with the account left active — the recoverable direction.
/// </para>
/// </remarks>
public sealed class AccountSuspendingHandler
{
    /// <summary>The agent, which owns the host's password database.</summary>
    private readonly IAgentSftpClient _agent;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that locks the account's logins.</param>
    public AccountSuspendingHandler(IAgentSftpClient agent)
    {
        _agent = agent;
    }

    /// <summary>Locks every SFTP login the host holds for the account.</summary>
    /// <param name="message">The account about to be suspended.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="InvalidOperationException">
    /// The agent refused. Thrown rather than returned, because a subscriber's only way to abort the
    /// suspension is to fail. The agent's own text is carried in the exception, which the Accounts
    /// handler logs and never answers a customer with (rules/security.md item 8).
    /// </exception>
    public async Task HandleAsync(AccountSuspending message, CancellationToken cancellationToken)
    {
        var locked = await _agent.SetAccountLoginsLockedAsync(message.Username, true, cancellationToken);
        if (!locked.IsSuccess)
        {
            throw new InvalidOperationException(
                $"the sftp logins of {message.Username} could not be locked: {locked.Error?.Code}");
        }
    }
}
