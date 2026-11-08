using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Interfaces;

namespace Maran.Modules.Mail.Mappers;

/// <summary>Restates the settings an administrator sent in the shape the agent's call takes.</summary>
public static class AgentMailServerSettingsMapper
{
    /// <summary>Builds the agent's settings for one validated input.</summary>
    /// <param name="input">The validated settings; every number is already in range.</param>
    /// <param name="hostname">The host name in the one spelling the panel stores, lower case.</param>
    /// <returns>
    /// The agent's settings. The webmail master hash is null: no webmail is part of this call, and a
    /// null tells the agent to leave the webmail login entry out.
    /// </returns>
    public static AgentMailServerSettings ToAgent(IMailServerSettingsInput input, string hostname)
    {
        return new AgentMailServerSettings(
            hostname,
            input.Pop3Enabled,
            input.AntivirusEnabled,
            checked((uint)input.MaxMessageMib),
            checked((uint)input.SpamGreylistScore),
            checked((uint)input.SpamAddHeaderScore),
            checked((uint)input.SpamRejectScore),
            input.AllowExternalForwarding,
            null);
    }
}
