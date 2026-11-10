namespace Maran.Agent.Client.Services.TransferService;

/// <summary>
/// Why a call to a registered source failed, for the cases the administrator is told apart. It rides
/// beside the failure's error code and never replaces it.
/// </summary>
public enum SourceFailureReason
{
    /// <summary>
    /// The failure is not about the source: this host failed, the agent could not be reached, or the
    /// request was refused as invalid. Also what a reason this build does not know is read as, because
    /// the error code beside it still states what happened.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The source presented a host key other than the pinned one: a possible interception, or the source
    /// was reinstalled. Never retried.
    /// </summary>
    HostKeyChanged = 1,

    /// <summary>
    /// The source refused the key: it is not in <c>authorized_keys</c>, the login is wrong, or a
    /// restriction rejected the connection.
    /// </summary>
    AuthRefused = 2,

    /// <summary>No SSH answer: refused, filtered, timed out or not resolvable.</summary>
    Unreachable = 3,

    /// <summary>
    /// The address is one the agent never connects to: loopback, link-local, unspecified, multicast, or
    /// one of this host's own addresses.
    /// </summary>
    AddressRefused = 4,
}
