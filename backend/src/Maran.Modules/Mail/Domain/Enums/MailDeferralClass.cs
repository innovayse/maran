namespace Maran.Modules.Mail.Domain.Enums;

/// <summary>
/// The class of reason a queued message is waiting, a closed set the agent derives from the MTA's own
/// reason text so that the text itself, which names hosts and paths, never reaches an API answer.
/// </summary>
public enum MailDeferralClass
{
    /// <summary>The message is not deferred; it has no failed attempt to explain.</summary>
    None,

    /// <summary>The remote host could not be reached.</summary>
    Network,

    /// <summary>The remote host answered and refused the message.</summary>
    RemoteRefused,

    /// <summary>The failure is on this host, not on the way to the remote one.</summary>
    LocalError,

    /// <summary>A reason the agent could not place in any other class.</summary>
    Other,
}
