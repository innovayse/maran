namespace Maran.Agent.Client.Services.FirewallService;

/// <summary>One address the host's firewall is currently dropping all traffic from.</summary>
/// <param name="Address">The banned IPv4 or IPv6 address, as the kernel holds it.</param>
/// <param name="ExpiresIn">
/// How much of the ban is left to run, or null when the ban has no timeout at all — permanent until
/// somebody unbans it. Null rather than a zero duration, because zero would otherwise have to mean
/// both "permanent" and "expiring this second", and the two call for opposite reconciliations by
/// the panel.
/// </param>
/// <remarks>
/// A remaining duration and not an expiry instant: what the kernel holds is a countdown, and
/// turning one into an absolute time needs a clock reading. The agent deliberately does not take
/// one, and neither does this project — the panel's own <c>IClock</c> is the only clock in the
/// system (rules/csharp.md, no ambient clock).
///
/// The reason a ban was placed is not here and cannot be: the agent stores none. The Firewall
/// module records the reason it gave when it asked for the ban, and that record is the only one.
/// </remarks>
public sealed record AgentFirewallBan(string Address, TimeSpan? ExpiresIn);
