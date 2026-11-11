using Maran.Modules.Mail.Domain.Enums;

namespace Maran.Modules.Mail.Domain.Entities;

/// <summary>
/// The panel's ledger of the local submission binding the agent holds for one system user (issue #518): which
/// users have a binding on the host, and whether it is current.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a ledger.</b> The agent can set and remove a binding but cannot list what it holds, so without a
/// record of its own the panel could never remove the binding of an account that no longer exists, and a push
/// that failed after a committed change would be forgotten. The row is keyed by system user name, the key the
/// agent uses, so an account created again under a recycled name takes the row over.
/// </para>
/// <para>
/// It belongs to the server and to no customer, carries no <c>AccountId</c>, and is never returned by an
/// endpoint; only the publisher and its reconciler read and write it.
/// </para>
/// </remarks>
public sealed class MailSubmissionBinding
{
    /// <summary>System username of the account; the row's identity.</summary>
    public string Username { get; private set; }

    /// <summary>What the panel knows about the agent's binding for this user.</summary>
    public MailSubmissionBindingState State { get; private set; }

    /// <summary>Creates the row for a system user.</summary>
    /// <param name="username">System username the agent addresses the binding by.</param>
    /// <param name="state">What the panel knows about the agent's binding.</param>
    public MailSubmissionBinding(string username, MailSubmissionBindingState state)
    {
        Username = username;
        State = state;
    }

    /// <summary>For EF Core.</summary>
    private MailSubmissionBinding()
    {
        Username = string.Empty;
    }

    /// <summary>Records what is now known about the agent's binding.</summary>
    /// <param name="state">The new state.</param>
    public void MarkAs(MailSubmissionBindingState state)
    {
        State = state;
    }
}
