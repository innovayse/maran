using Maran.Modules.Mail.Domain.Enums;

namespace Maran.Modules.Mail.Common;

/// <summary>
/// The mail server as the administrator's screen shows it: the panel's intention beside what the
/// host was observed to do, never merged.
/// </summary>
/// <param name="Settings">What was asked for; null on a server where mail was never configured, which is not "turned off".</param>
/// <param name="Ports">The TCP ports an operator opens in the firewall for mail to work; this module opens none.</param>
/// <param name="ObservedEnabled">Whether the host's mail stack was observed to be switched on.</param>
/// <param name="Daemons">What the host reports for each mail daemon.</param>
/// <param name="EffectiveHostname">The host name the running configuration presents; empty when it was not read.</param>
/// <param name="TlsInUse">Whether the daemons were observed using the panel's certificate.</param>
/// <param name="QueuedMessages">The number of messages in the queue, or null when it was not read.</param>
/// <param name="PasswordScheme">The scheme the host verifies mailbox passwords in, or null when it was not read.</param>
/// <param name="SieveAvailable">Whether the host can run autoresponder scripts.</param>
/// <param name="StartingValues">What a server that is off is turned on with when the administrator names nothing else.</param>
public sealed record MailServerStatusDto(
    MailServerSettingsDto? Settings,
    IReadOnlyList<int> Ports,
    MailObservation ObservedEnabled,
    IReadOnlyList<MailDaemonStatusDto> Daemons,
    string EffectiveHostname,
    MailObservation TlsInUse,
    uint? QueuedMessages,
    MailPasswordScheme? PasswordScheme,
    MailObservation SieveAvailable,
    MailServerStartingValuesDto StartingValues);
