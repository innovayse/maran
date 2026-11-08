using Maran.Modules.Mail.Domain.Enums;

namespace Maran.Modules.Mail.Common;

/// <summary>What the host reports about one mail daemon.</summary>
/// <param name="Kind">Which daemon.</param>
/// <param name="Installed">Whether the daemon's program is installed.</param>
/// <param name="Version">The installed version as the host reports it; empty when it is not installed or not read.</param>
/// <param name="Active">Whether the daemon's unit is running.</param>
/// <param name="Listeners">The addresses and ports it listens on.</param>
/// <param name="ListenersObserved">Whether <paramref name="Listeners"/> was read; an empty list is "none" only when this is Yes.</param>
public sealed record MailDaemonStatusDto(
    MailDaemonKind Kind,
    MailObservation Installed,
    string Version,
    MailObservation Active,
    IReadOnlyList<MailListenerDto> Listeners,
    MailObservation ListenersObserved);
