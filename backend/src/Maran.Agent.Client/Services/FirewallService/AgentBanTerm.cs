namespace Maran.Agent.Client.Services.FirewallService;

/// <summary>
/// How long a ban lasts, as the caller states it: exactly one of two cases, each its own type, so a
/// duration exists only on the case that has one and "until somebody lifts it" is a case of its own
/// rather than the absence of a duration.
/// </summary>
/// <remarks>
/// Before this type a ban's length was a <c>TimeSpan?</c> in which <c>null</c> meant permanent, and
/// on the wire a <c>duration_seconds</c> of zero meant the same: a value that was merely missing or
/// truncated to nothing arrived as the opposite of a short ban. Here the wish for a permanent ban
/// must be spelled out, and the client sends it as the contract's own <c>permanent</c> case. The
/// constructor is private to this assembly, so the two nested cases are the whole set.
/// </remarks>
public abstract record AgentBanTerm
{
    /// <summary>Closes the hierarchy to the two cases declared below.</summary>
    private AgentBanTerm()
    {
    }

    /// <summary>The ban lasts until somebody lifts it.</summary>
    public sealed record Permanent : AgentBanTerm;

    /// <summary>The ban lasts for a fixed time and then ends by itself.</summary>
    /// <param name="Duration">
    /// How long the ban lasts. The wire carries whole seconds, so a fraction is dropped: 90.7 seconds
    /// installs a 90-second ban, and never a longer one. A duration under one second, a negative one
    /// and one longer than the field can hold are refused by the client rather than rounded into a
    /// different ban.
    /// </param>
    public sealed record ExpiresAfter(TimeSpan Duration) : AgentBanTerm;
}
