using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Entities;

namespace Maran.Modules.Mail.Mappers;

/// <summary>Restates a <see cref="MailAutoresponder"/> as the wire shape and as the agent's arguments.</summary>
public static class MailAutoresponderMapper
{
    /// <summary>Builds the outward view of one autoresponder.</summary>
    /// <param name="responder">The recorded autoresponder.</param>
    /// <returns>The wire shape carrying exactly what the row holds.</returns>
    public static MailAutoresponderDto From(MailAutoresponder responder)
    {
        ArgumentNullException.ThrowIfNull(responder);

        return new MailAutoresponderDto(
            responder.MailboxId,
            responder.Subject,
            responder.Body,
            responder.StartsAt,
            responder.EndsAt,
            responder.ReplyIntervalDays);
    }

    /// <summary>The agent's description of the reply a row holds.</summary>
    /// <param name="responder">The recorded autoresponder.</param>
    /// <returns>The arguments the agent installs the reply from.</returns>
    public static AgentMailAutoresponder ToAgent(MailAutoresponder responder)
    {
        ArgumentNullException.ThrowIfNull(responder);

        return new AgentMailAutoresponder(
            responder.Subject,
            responder.Body,
            responder.StartsAt,
            responder.EndsAt,
            (uint?)responder.ReplyIntervalDays);
    }
}
