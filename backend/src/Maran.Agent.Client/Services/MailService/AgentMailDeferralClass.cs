namespace Maran.Agent.Client.Services.MailService;

/// <summary>A closed classification of why a message was deferred.</summary>
/// <remarks>
/// A panel-side mirror of the wire's <c>MailDeferralClass</c>. The agent maps the MTA's free-text reason
/// onto this set so that no remote server's words reach a screen. The wire's UNSPECIFIED has no member:
/// an entry that states no class is refused as an invalid response.
/// </remarks>
public enum AgentMailDeferralClass
{
    /// <summary>Not deferred: no delivery attempt has failed yet.</summary>
    None = 1,

    /// <summary>The remote host could not be reached, resolved or timed out.</summary>
    Network = 2,

    /// <summary>The remote host answered with a temporary refusal.</summary>
    RemoteRefused = 3,

    /// <summary>This host could not deliver locally (quota, disk, a daemon down).</summary>
    LocalError = 4,

    /// <summary>Any reason the agent does not classify.</summary>
    Other = 5,
}
