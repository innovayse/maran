using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Domain.Policies;

namespace Maran.Modules.Mail.Mappers;

/// <summary>
/// Restates the agent's observation of the mail stack, beside the settings row, as the wire shape of
/// the status screen.
/// </summary>
/// <remarks>
/// A mapper translates and never decides. The intention and the observation are carried side by
/// side and no field is derived from both, so the screen cannot agree with the panel by
/// construction; and no text the agent wrote reaches the answer, only the closed vocabularies its
/// wire types are mirrored by here.
/// </remarks>
public static class MailServerStatusMapper
{
    /// <summary>Builds the status for one observation and the settings row, if there is one.</summary>
    /// <param name="observed">What the agent saw on the host.</param>
    /// <param name="settings">The settings row, or null on a server where mail was never configured.</param>
    /// <returns>The wire shape.</returns>
    public static MailServerStatusDto ToDto(AgentMailStatus observed, MailServerSettings? settings)
    {
        return new MailServerStatusDto(
            settings is null ? null : ToSettingsDto(settings),
            MailDefaults.ExpectedPorts,
            ToObservation(observed.Enabled),
            observed.Daemons
                .Select(daemon =>
                {
                    return ToDaemonDto(daemon);
                })
                .ToList(),
            observed.EffectiveHostname,
            ToObservation(observed.TlsInUse),
            observed.QueuedMessages,
            observed.PasswordScheme is { } scheme ? ToPasswordScheme(scheme) : null,
            ToObservation(observed.SieveAvailable));
    }

    /// <summary>Restates the settings row.</summary>
    /// <param name="settings">The row.</param>
    /// <returns>The wire shape.</returns>
    private static MailServerSettingsDto ToSettingsDto(MailServerSettings settings)
    {
        return new MailServerSettingsDto(
            settings.Enabled,
            settings.Hostname,
            settings.Pop3Enabled,
            settings.AntivirusEnabled,
            settings.AllowExternalForwarding,
            settings.MaxMessageMib,
            settings.SpamGreylistScore,
            settings.SpamAddHeaderScore,
            settings.SpamRejectScore,
            settings.UpdatedAt);
    }

    /// <summary>Restates one daemon's observed state.</summary>
    /// <param name="daemon">The agent's report on the daemon.</param>
    /// <returns>The wire shape.</returns>
    private static MailDaemonStatusDto ToDaemonDto(AgentMailDaemonStatus daemon)
    {
        return new MailDaemonStatusDto(
            ToKind(daemon.Daemon),
            ToObservation(daemon.Installed),
            daemon.Version,
            ToObservation(daemon.Active),
            daemon.Listeners
                .Select(listener =>
                {
                    return new MailListenerDto(listener.Address, listener.Port);
                })
                .ToList(),
            ToObservation(daemon.ListenersObserved));
    }

    /// <summary>Mirrors the agent's observation vocabulary.</summary>
    /// <param name="observed">The agent's value.</param>
    /// <returns>This module's value.</returns>
    private static MailObservation ToObservation(AgentMailObservation observed)
    {
        return observed switch
        {
            AgentMailObservation.NotObserved => MailObservation.NotObserved,
            AgentMailObservation.Yes => MailObservation.Yes,
            AgentMailObservation.No => MailObservation.No,
            _ => throw new ArgumentOutOfRangeException(nameof(observed), observed, "Unknown mail observation."),
        };
    }

    /// <summary>Mirrors the agent's daemon vocabulary.</summary>
    /// <param name="daemon">The agent's value.</param>
    /// <returns>This module's value.</returns>
    private static MailDaemonKind ToKind(AgentMailDaemon daemon)
    {
        return daemon switch
        {
            AgentMailDaemon.Mta => MailDaemonKind.Mta,
            AgentMailDaemon.ImapServer => MailDaemonKind.ImapServer,
            AgentMailDaemon.SpamFilter => MailDaemonKind.SpamFilter,
            AgentMailDaemon.RateLimitStore => MailDaemonKind.RateLimitStore,
            AgentMailDaemon.VirusScanner => MailDaemonKind.VirusScanner,
            _ => throw new ArgumentOutOfRangeException(nameof(daemon), daemon, "Unknown mail daemon."),
        };
    }

    /// <summary>Mirrors the agent's password scheme vocabulary.</summary>
    /// <param name="scheme">The agent's value.</param>
    /// <returns>This module's value.</returns>
    private static MailPasswordScheme ToPasswordScheme(AgentMailPasswordScheme scheme)
    {
        return scheme switch
        {
            AgentMailPasswordScheme.Argon2id => MailPasswordScheme.Argon2id,
            AgentMailPasswordScheme.BlowfishCrypt => MailPasswordScheme.BlowfishCrypt,
            _ => throw new ArgumentOutOfRangeException(nameof(scheme), scheme, "Unknown mail password scheme."),
        };
    }
}
