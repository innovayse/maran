using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Mail.Mappers;

/// <summary>Restates a <see cref="Mailbox"/> as the arguments the mail host is given.</summary>
/// <remarks>
/// The mailbox is addressed to the agent by the ACCOUNT's system user name and the domain NAME, never by
/// a fully formed address built elsewhere, so a request cannot express another account's mailbox. The
/// password hash is the only secret that travels, wrapped in <see cref="SensitiveString"/>; a
/// <c>null</c> hash is the agent's "keep the current one".
/// </remarks>
public static class MailboxAgentMapper
{
    /// <summary>The arguments that make the host hold the mailbox exactly as the row says.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="domainName">The name of the domain the mailbox lives in.</param>
    /// <param name="mailbox">The mailbox whose current limits and switch are sent.</param>
    /// <param name="passwordHash">The hash to install, or <c>null</c> to keep the host's current one.</param>
    /// <returns>The agent call's arguments.</returns>
    public static AgentMailboxArguments Current(
        string accountUsername,
        string domainName,
        Mailbox mailbox,
        SensitiveString? passwordHash)
    {
        ArgumentNullException.ThrowIfNull(mailbox);

        return Replacing(
            accountUsername,
            domainName,
            mailbox,
            passwordHash,
            mailbox.QuotaMib,
            mailbox.SendLimitPerHour,
            mailbox.Enabled);
    }

    /// <summary>The arguments that make the host hold the mailbox with new limits and switch.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="domainName">The name of the domain the mailbox lives in.</param>
    /// <param name="mailbox">The mailbox, which supplies its local part and spam action.</param>
    /// <param name="passwordHash">The hash to install, or <c>null</c> to keep the host's current one.</param>
    /// <param name="quotaMib">The size limit to send, in MiB.</param>
    /// <param name="sendLimitPerHour">The hourly send limit to send.</param>
    /// <param name="enabled">Whether the owner has the mailbox switched on.</param>
    /// <returns>The agent call's arguments.</returns>
    public static AgentMailboxArguments Replacing(
        string accountUsername,
        string domainName,
        Mailbox mailbox,
        SensitiveString? passwordHash,
        int quotaMib,
        int sendLimitPerHour,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(mailbox);

        return new AgentMailboxArguments(
            accountUsername,
            domainName,
            mailbox.LocalPart,
            passwordHash,
            (uint)quotaMib,
            !enabled,
            (uint)sendLimitPerHour,
            ToAgent(mailbox.SpamAction));
    }

    /// <summary>The agent's spelling of what is done with mail the spam filter flags.</summary>
    /// <param name="action">The row's spam action.</param>
    /// <returns>The agent client's equivalent.</returns>
    private static AgentMailSpamAction ToAgent(MailSpamAction action)
    {
        return action switch
        {
            MailSpamAction.FileToJunk => AgentMailSpamAction.FileToJunk,
            MailSpamAction.KeepInInbox => AgentMailSpamAction.KeepInInbox,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown spam action."),
        };
    }
}
