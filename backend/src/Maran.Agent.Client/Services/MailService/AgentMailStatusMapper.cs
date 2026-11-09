using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.MailService;

/// <summary>Reads the wire's mail status into the panel's own, once, at the client boundary.</summary>
/// <remarks>
/// Every "the wire says nothing" case is decided here and nowhere else, so the panel's model never
/// carries a protobuf zero as a verdict: an unspecified observation is <see cref="AgentMailObservation.NotObserved"/>,
/// an unspecified password scheme and a zero version are <c>null</c>, and an absent queue is <c>null</c>,
/// never zero messages (contract revision rule, rules/proto.md).
/// </remarks>
internal static class AgentMailStatusMapper
{
    /// <summary>Projects the wire status onto the panel's own.</summary>
    /// <param name="status">The wire status; an absent message reads as an empty one and is refused by the caller's check.</param>
    /// <returns>
    /// The status, or <c>null</c> when a daemon entry names no known daemon: an answer that cannot be
    /// read in full is refused and never given a name the agent did not state.
    /// </returns>
    internal static AgentMailStatus? ToStatus(MailStatus status)
    {
        var daemons = new List<AgentMailDaemonStatus>(status.Daemons.Count);
        foreach (var wire in status.Daemons)
        {
            var daemon = ToDaemon(wire.Daemon);
            if (daemon is null)
            {
                return null;
            }

            daemons.Add(new AgentMailDaemonStatus(
                daemon.Value,
                ToObservation(wire.Installed),
                wire.Version,
                ToObservation(wire.Active),
                wire.Listeners.Select(listener => { return new AgentMailListener(listener.Address, listener.Port); })
                    .ToList(),
                ToObservation(wire.ListenersObserved)));
        }

        return new AgentMailStatus(
            daemons,
            ToObservation(status.Enabled),
            status.EffectiveHostname,
            ToObservation(status.TlsInUse),
            status.DovecotMajor == 0 ? null : status.DovecotMajor,
            status.DovecotMajor == 0 ? null : status.DovecotMinor,
            ToPasswordScheme(status.PasswordScheme),
            ToObservation(status.PasswordSchemeVerified),
            status.Queue?.Messages,
            ToObservation(status.SieveAvailable));
    }

    /// <summary>Projects the wire observation onto the panel's own.</summary>
    /// <param name="observation">The wire value.</param>
    /// <returns>The panel's value; <see cref="AgentMailObservation.NotObserved"/> for anything unnamed.</returns>
    internal static AgentMailObservation ToObservation(MailObservation observation)
    {
        return observation switch
        {
            MailObservation.Yes => AgentMailObservation.Yes,
            MailObservation.No => AgentMailObservation.No,
            _ => AgentMailObservation.NotObserved,
        };
    }

    /// <summary>Projects the wire daemon onto the panel's own.</summary>
    /// <param name="daemon">The wire value.</param>
    /// <returns>The panel's value, or <c>null</c> for the contract's zero value or one this build does not know.</returns>
    private static AgentMailDaemon? ToDaemon(MailDaemon daemon)
    {
        return daemon switch
        {
            MailDaemon.Mta => AgentMailDaemon.Mta,
            MailDaemon.ImapServer => AgentMailDaemon.ImapServer,
            MailDaemon.SpamFilter => AgentMailDaemon.SpamFilter,
            MailDaemon.RateLimitStore => AgentMailDaemon.RateLimitStore,
            MailDaemon.VirusScanner => AgentMailDaemon.VirusScanner,
            _ => null,
        };
    }

    /// <summary>Projects the wire password scheme onto the panel's own.</summary>
    /// <param name="scheme">The wire value.</param>
    /// <returns>The panel's value, or <c>null</c> for "not known" and for a scheme this build does not know.</returns>
    private static AgentMailPasswordScheme? ToPasswordScheme(MailPasswordScheme scheme)
    {
        return scheme switch
        {
            MailPasswordScheme.Argon2Id => AgentMailPasswordScheme.Argon2id,
            MailPasswordScheme.BlfCrypt => AgentMailPasswordScheme.BlowfishCrypt,
            _ => null,
        };
    }
}
