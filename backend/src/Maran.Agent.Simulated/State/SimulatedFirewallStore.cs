using System.Net;
using Maran.Agent.Client.Services.FirewallService;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.Limits;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Agent.Simulated.State;

/// <summary>
/// The port rules and address bans of the simulated firewall, bounded and safe to share between
/// requests. Nothing is enforced: no packet is dropped because there is none.
/// </summary>
/// <remarks>
/// The refusals match the real client's, so a screen shows the same answer to the same input — an
/// unusable SSH port or panel port is <c>AgentFirewallPortsMisconfigured</c> even here, because that
/// is a statement about the panel's own configuration and the simulation has the same configuration.
/// </remarks>
public sealed class SimulatedFirewallStore
{
    /// <summary>The lowest port number a rule may name.</summary>
    private const int LowestPort = 1;

    /// <summary>The highest port number a rule may name.</summary>
    private const int HighestPort = 65_535;

    /// <summary>Serialises every read and write of the rules and the bans.</summary>
    private readonly Lock _gate = new();

    /// <summary>The ceilings this store enforces.</summary>
    private readonly SimulatedAgentLimits _limits;

    /// <summary>The port rules in the order they were allowed.</summary>
    private readonly List<AgentFirewallRule> _rules = [];

    /// <summary>The bans keyed by address; a null expiry is a permanent ban.</summary>
    private readonly Dictionary<string, DateTimeOffset?> _bans = [];

    /// <summary>Creates an empty store bounded by <paramref name="limits"/>.</summary>
    /// <param name="limits">The ceilings this store enforces.</param>
    public SimulatedFirewallStore(SimulatedAgentLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        _limits = limits;
    }

    /// <summary>Lists the port rules somebody asked for.</summary>
    /// <param name="sshPorts">Every port the host's sshd listens on.</param>
    /// <param name="panelPort">The public port the panel is reachable on.</param>
    /// <returns>The rules in the order they were allowed, or <c>AgentFirewallPortsMisconfigured</c>.</returns>
    public Result<IReadOnlyList<AgentFirewallRule>> ListRules(IReadOnlyList<int> sshPorts, int panelPort)
    {
        if (!AreHostPortsUsable(sshPorts, panelPort))
        {
            return Result<IReadOnlyList<AgentFirewallRule>>.Fail(SimulatedAgentErrors.FirewallPortsMisconfigured());
        }

        lock (_gate)
        {
            return Result<IReadOnlyList<AgentFirewallRule>>.Ok(_rules.ToList());
        }
    }

    /// <summary>Allows traffic to a port or a range of ports, optionally scoped to one source range.</summary>
    /// <param name="port">The port, or the lower bound of a range.</param>
    /// <param name="portTo">The inclusive upper bound of a range, or <c>null</c> for a single port.</param>
    /// <param name="protocol">The transport protocol.</param>
    /// <param name="sourceCidr">The source range; <c>0.0.0.0/0</c> allows any source.</param>
    /// <param name="sshPorts">Every port the host's sshd listens on.</param>
    /// <param name="panelPort">The public port the panel is reachable on.</param>
    /// <returns>
    /// Success; <c>AgentAlreadyExists</c> for an identical rule; <c>AgentInvalidInput</c> for an
    /// unusable port, range or source; <c>AgentFirewallPortsMisconfigured</c> for unusable host ports;
    /// <c>AgentLimitReached</c> when the firewall holds as many rules as it will.
    /// </returns>
    public Result Allow(
        int port,
        int? portTo,
        AgentFirewallProtocol protocol,
        string sourceCidr,
        IReadOnlyList<int> sshPorts,
        int panelPort)
    {
        var refusal = Refuse(port, portTo, protocol, sourceCidr, sshPorts, panelPort, out var rule);
        if (refusal is not null)
        {
            return Result.Fail(refusal);
        }

        lock (_gate)
        {
            if (_rules.Contains(rule!))
            {
                return Result.Fail(SimulatedAgentErrors.AlreadyExists());
            }

            if (_rules.Count >= _limits.MaxFirewallRules)
            {
                return Result.Fail(SimulatedAgentErrors.LimitReached());
            }

            _rules.Add(rule!);

            return Result.Ok();
        }
    }

    /// <summary>Removes an allow, matching the whole value it was installed with.</summary>
    /// <param name="port">The port, or the lower bound of the range.</param>
    /// <param name="portTo">The inclusive upper bound of the range, or <c>null</c> for a single port.</param>
    /// <param name="protocol">The transport protocol.</param>
    /// <param name="sourceCidr">The source range the allow was scoped to.</param>
    /// <param name="sshPorts">Every port the host's sshd listens on.</param>
    /// <param name="panelPort">The public port the panel is reachable on.</param>
    /// <returns>
    /// Success — denying a port that is not allowed is a no-op — or the refusals <see cref="Allow"/> raises.
    /// </returns>
    public Result Deny(
        int port,
        int? portTo,
        AgentFirewallProtocol protocol,
        string sourceCidr,
        IReadOnlyList<int> sshPorts,
        int panelPort)
    {
        var refusal = Refuse(port, portTo, protocol, sourceCidr, sshPorts, panelPort, out var rule);
        if (refusal is not null)
        {
            return Result.Fail(refusal);
        }

        lock (_gate)
        {
            _rules.Remove(rule!);

            return Result.Ok();
        }
    }

    /// <summary>Bans an address until the ban is lifted or runs out; banning it again replaces the term.</summary>
    /// <param name="address">The IPv4 or IPv6 address to ban.</param>
    /// <param name="term">How long the ban lasts.</param>
    /// <param name="now">The instant a timed ban starts counting from.</param>
    /// <returns>
    /// Success; <c>AgentInvalidInput</c> for an address that does not parse or a timed term under one
    /// second, negative or longer than the wire can carry; <c>AgentLimitReached</c> when the firewall
    /// holds as many bans as it will.
    /// </returns>
    public Result Ban(string address, AgentBanTerm term, DateTimeOffset now)
    {
        if (!TryNormaliseAddress(address, out var key) || !TryExpiry(term, now, out var expiresAt))
        {
            return Result.Fail(SimulatedAgentErrors.InvalidInput());
        }

        lock (_gate)
        {
            PurgeExpired(now);
            if (!_bans.ContainsKey(key) && _bans.Count >= _limits.MaxFirewallBans)
            {
                return Result.Fail(SimulatedAgentErrors.LimitReached());
            }

            _bans[key] = expiresAt;

            return Result.Ok();
        }
    }

    /// <summary>Lifts a ban.</summary>
    /// <param name="address">The address to unban.</param>
    /// <param name="now">The instant that decides which bans have already run out.</param>
    /// <returns>
    /// Success, <c>AgentInvalidInput</c> for an address that does not parse, or <c>AgentNotFound</c> when there was no
    /// active ban.
    /// </returns>
    public Result Unban(string address, DateTimeOffset now)
    {
        if (!TryNormaliseAddress(address, out var key))
        {
            return Result.Fail(SimulatedAgentErrors.InvalidInput());
        }

        lock (_gate)
        {
            PurgeExpired(now);

            return _bans.Remove(key) ? Result.Ok() : Result.Fail(SimulatedAgentErrors.NotFound());
        }
    }

    /// <summary>Lists the bans in force, each with the whole seconds it has left to run.</summary>
    /// <param name="now">The instant that decides which bans have already run out.</param>
    /// <returns>The bans ordered by address; a permanent ban carries no time left.</returns>
    public IReadOnlyList<AgentFirewallBan> ListBans(DateTimeOffset now)
    {
        lock (_gate)
        {
            PurgeExpired(now);

            return _bans
                .OrderBy(ban => { return ban.Key; }, StringComparer.Ordinal)
                .Select(ban =>
                {
                    var remaining = ban.Value is null
                        ? (TimeSpan?)null
                        : TimeSpan.FromSeconds(Math.Floor((ban.Value.Value - now).TotalSeconds));

                    return new AgentFirewallBan(ban.Key, remaining);
                })
                .ToList();
        }
    }

    /// <summary>Whether the SSH ports and the panel port are all inside the range a port can take.</summary>
    /// <param name="sshPorts">Every port the host's sshd listens on.</param>
    /// <param name="panelPort">The public port the panel is reachable on.</param>
    /// <returns><c>true</c> when at least one SSH port is given and every port is usable.</returns>
    private static bool AreHostPortsUsable(IReadOnlyList<int> sshPorts, int panelPort)
    {
        return sshPorts.Count > 0 && sshPorts.All(IsUsablePort) && IsUsablePort(panelPort);
    }

    /// <summary>Whether a number can name a port.</summary>
    /// <param name="port">The number.</param>
    /// <returns><c>true</c> for 1 to 65535.</returns>
    private static bool IsUsablePort(int port)
    {
        return port is >= LowestPort and <= HighestPort;
    }

    /// <summary>Parses an address into the one spelling bans are keyed by.</summary>
    /// <param name="address">The address as the caller sent it.</param>
    /// <param name="key">The address without a scope or an IPv4-mapped wrapper, on success.</param>
    /// <returns><c>false</c> when the text is not an IPv4 or IPv6 address.</returns>
    private static bool TryNormaliseAddress(string address, out string key)
    {
        key = string.Empty;
        if (!IPAddress.TryParse(address, out var parsed))
        {
            return false;
        }

        key = Ipv4MappedAddress.Unwrap(ScopelessAddress.Strip(parsed)).ToString();

        return true;
    }

    /// <summary>Turns a ban term into the instant the ban runs out.</summary>
    /// <param name="term">The term.</param>
    /// <param name="now">The instant a timed ban starts counting from.</param>
    /// <param name="expiresAt">The expiry, or <c>null</c> for a permanent ban, on success.</param>
    /// <returns>
    /// <c>false</c> for a timed term under one second, negative or longer than the wire's whole seconds hold.
    /// </returns>
    private static bool TryExpiry(AgentBanTerm term, DateTimeOffset now, out DateTimeOffset? expiresAt)
    {
        expiresAt = null;
        if (term is not AgentBanTerm.ExpiresAfter expiresAfter)
        {
            return term is AgentBanTerm.Permanent;
        }

        var seconds = Math.Floor(expiresAfter.Duration.TotalSeconds);
        if (seconds < 1 || seconds > uint.MaxValue)
        {
            return false;
        }

        expiresAt = now.AddSeconds(seconds);

        return true;
    }

    /// <summary>Checks a rule's arguments in the order the real client does and builds its canonical value.</summary>
    /// <param name="port">The port, or the lower bound of a range.</param>
    /// <param name="portTo">The inclusive upper bound of a range, or <c>null</c>.</param>
    /// <param name="protocol">The transport protocol.</param>
    /// <param name="sourceCidr">The source range.</param>
    /// <param name="sshPorts">Every port the host's sshd listens on.</param>
    /// <param name="panelPort">The public port the panel is reachable on.</param>
    /// <param name="rule">The rule in its canonical form, when nothing was refused.</param>
    /// <returns>The refusal, or <c>null</c> when the arguments are usable.</returns>
    private static Error? Refuse(
        int port,
        int? portTo,
        AgentFirewallProtocol protocol,
        string sourceCidr,
        IReadOnlyList<int> sshPorts,
        int panelPort,
        out AgentFirewallRule? rule)
    {
        rule = null;
        if (!IsUsablePort(port) || (portTo is not null && (!IsUsablePort(portTo.Value) || portTo.Value <= port)))
        {
            return SimulatedAgentErrors.InvalidInput();
        }

        if (!AreHostPortsUsable(sshPorts, panelPort))
        {
            return SimulatedAgentErrors.FirewallPortsMisconfigured();
        }

        if (!CidrRule.TryParse(sourceCidr, out var cidr))
        {
            return SimulatedAgentErrors.InvalidInput();
        }

        rule = new AgentFirewallRule(port, portTo, protocol, cidr!.ToString());

        return null;
    }

    /// <summary>Drops every timed ban that has run out; the caller holds <see cref="_gate"/>.</summary>
    /// <param name="now">The instant that decides which bans have run out.</param>
    private void PurgeExpired(DateTimeOffset now)
    {
        var expired = _bans.Where(ban => { return ban.Value is not null && ban.Value <= now; }).Select(ban =>
        {
            return ban.Key;
        }).ToList();
        foreach (var address in expired)
        {
            _bans.Remove(address);
        }
    }
}
