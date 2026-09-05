using Google.Protobuf.Collections;
using Grpc.Net.Client;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.FirewallService;

/// <summary>Maps the agent's firewall rpcs onto <see cref="Result{T}"/>.</summary>
/// <remarks>
/// Same shape as the other agent clients: the failure branch of the response oneof becomes a typed
/// <see cref="Error"/> carrying only a code, and the agent's own diagnostic text is logged rather
/// than returned (rules/security.md item 8).
///
/// This client refuses two things BEFORE it sends them, which the others do not, and both refusals
/// exist because the wire type cannot express "absent". The host's SSH ports and the panel's port
/// are re-rendered into a drop-policy ruleset on every mutation, so a request that omitted them
/// would arrive as zeros and — on any peer that defaulted them — close the operator's own session
/// with no remote way back in. And a ban's duration is whole seconds on the wire, where 0 means
/// permanent, so a sub-second duration truncated on the way out would arrive as a ban nobody can
/// wait out. The agent refuses all of these too; refusing here as well means the panel never sends
/// a request whose worst outcome depends on the peer behaving.
///
/// Nothing here substitutes a value for a missing one. There is no fallback SSH port in this file,
/// and there must never be: the installer already falls back to 22 and says so in its log when
/// detection finds nothing, so an empty list arriving here means something upstream broke, and a
/// 22 invented at this depth would render an accept for a port nothing listens on and none for the
/// port the operator is connected through.
///
/// The two refusals answer with DIFFERENT codes, and deliberately: unusable host facts are
/// <c>AgentFirewallPortsMisconfigured</c>, a bad argument is <c>AgentInvalidInput</c>. One is fixed
/// by repairing <c>panel.env</c> and one by retyping a port, so a caller that saw a single code
/// would have to tell an administrator to check details they never entered.
/// </remarks>
public sealed class AgentFirewallClient : IAgentFirewallClient
{
    /// <summary>The lowest port number a rule may name.</summary>
    private const int LowestPort = 1;

    /// <summary>The highest port number a rule may name.</summary>
    private const int HighestPort = 65535;

    /// <summary>
    /// Log delegate for a refusal caused by a missing or unusable HOST fact — the SSH ports or the
    /// panel port. Error rather than warning: those values are written into <c>panel.env</c> by the
    /// installer and validated when the module starts, so one arriving unusable here is a broken
    /// deployment rather than a bad request, and nothing in the firewall will work until it is fixed.
    /// </summary>
    private static readonly Action<ILogger, string, string, Exception?> LogRefusedHostFact =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1, nameof(AgentFirewallClient)),
            "Refused {Operation} without sending it: {Reason}. The host's SSH ports and the panel's "
            + "port are written into panel.env by the installer and are never defaulted here.");

    /// <summary>
    /// Log delegate for a refusal caused by a value this call was ASKED for. A warning, because it
    /// is a bad request rather than a broken host, and the caller is told by the returned error.
    /// </summary>
    private static readonly Action<ILogger, string, string, Exception?> LogRefusedArgument =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(2, nameof(AgentFirewallClient)),
            "Refused {Operation} without sending it: {Reason}");

    /// <summary>The transport seam this client drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly IFirewallServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<AgentFirewallClient> _logger;

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructor).</summary>
    /// <param name="invoker">The transport that performs the actual calls.</param>
    /// <param name="logger">Sink for the agent's diagnostic text and for a local refusal.</param>
    internal AgentFirewallClient(IFirewallServiceInvoker invoker, ILogger<AgentFirewallClient> logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Creates a client that calls the agent over <paramref name="channel"/>.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text and for a local refusal.</param>
    public AgentFirewallClient(GrpcChannel channel, ILogger<AgentFirewallClient> logger)
        : this(new GrpcFirewallServiceInvoker(new V1.FirewallService.FirewallServiceClient(channel)), logger)
    {
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentFirewallRule>>> ListRulesAsync(
        IReadOnlyList<int> sshPorts,
        int panelPort,
        CancellationToken cancellationToken)
    {
        var refusal = RefuseUnusableHostPorts(nameof(ListRulesAsync), sshPorts, panelPort);
        if (refusal is not null)
        {
            return Result<IReadOnlyList<AgentFirewallRule>>.Fail(refusal);
        }

        var request = new ListRulesRequest { PanelPort = (uint)panelPort };
        AddSshPorts(request.SshPorts, sshPorts);

        var response = await _invoker.ListRulesAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            ListRulesResponse.ResultOneofCase.Ok => ToRulesResult(response.Ok),
            ListRulesResponse.ResultOneofCase.Error => Result<IReadOnlyList<AgentFirewallRule>>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ListRulesAsync))),
            _ => Result<IReadOnlyList<AgentFirewallRule>>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> AllowPortAsync(
        int port,
        AgentFirewallProtocol protocol,
        string sourceCidr,
        IReadOnlyList<int> sshPorts,
        int panelPort,
        CancellationToken cancellationToken)
    {
        var refusal = RefuseUnusableRulePort(nameof(AllowPortAsync), port)
            ?? RefuseUnusableHostPorts(nameof(AllowPortAsync), sshPorts, panelPort);
        if (refusal is not null)
        {
            return Result<bool>.Fail(refusal);
        }

        var request = new AllowPortRequest
        {
            Port = (uint)port,
            Protocol = ToWireProtocol(protocol),
            SourceCidr = sourceCidr,
            PanelPort = (uint)panelPort,
        };
        AddSshPorts(request.SshPorts, sshPorts);

        var response = await _invoker.AllowPortAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            AllowPortResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            AllowPortResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(AllowPortAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DenyPortAsync(
        int port,
        AgentFirewallProtocol protocol,
        string sourceCidr,
        IReadOnlyList<int> sshPorts,
        int panelPort,
        CancellationToken cancellationToken)
    {
        var refusal = RefuseUnusableRulePort(nameof(DenyPortAsync), port)
            ?? RefuseUnusableHostPorts(nameof(DenyPortAsync), sshPorts, panelPort);
        if (refusal is not null)
        {
            return Result<bool>.Fail(refusal);
        }

        var request = new DenyPortRequest
        {
            Port = (uint)port,
            Protocol = ToWireProtocol(protocol),
            SourceCidr = sourceCidr,
            PanelPort = (uint)panelPort,
        };
        AddSshPorts(request.SshPorts, sshPorts);

        var response = await _invoker.DenyPortAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            DenyPortResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            DenyPortResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(DenyPortAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> BanAsync(string address, TimeSpan? ttl, CancellationToken cancellationToken)
    {
        if (!TryToDurationSeconds(ttl, out var durationSeconds))
        {
            LogRefusedArgument(
                _logger,
                nameof(BanAsync),
                "a ban's duration is whole seconds on the wire and 0 there means permanent, so a "
                + "duration under one second, a negative one, or one longer than the wire can carry "
                + "is refused rather than rounded into a different ban",
                null);

            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidInput), ErrorType.Validation));
        }

        var request = new BanAddressRequest
        {
            Address = address,
            DurationSeconds = durationSeconds,

            // Reason is deliberately not set: the agent never reads it, and the only place it could
            // go on that side is an nftables comment, whose argument nft parses in its own grammar.
        };
        var response = await _invoker.BanAddressAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            BanAddressResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            BanAddressResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(BanAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> UnbanAsync(string address, CancellationToken cancellationToken)
    {
        var request = new UnbanAddressRequest { Address = address };
        var response = await _invoker.UnbanAddressAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            UnbanAddressResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            UnbanAddressResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(UnbanAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentFirewallBan>>> ListBansAsync(CancellationToken cancellationToken)
    {
        var response = await _invoker.ListBansAsync(new ListBansRequest(), cancellationToken);

        return response.ResultCase switch
        {
            ListBansResponse.ResultOneofCase.Ok => Result<IReadOnlyList<AgentFirewallBan>>.Ok(
                ToBans(response.Ok)),
            ListBansResponse.ResultOneofCase.Error => Result<IReadOnlyList<AgentFirewallBan>>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ListBansAsync))),
            _ => Result<IReadOnlyList<AgentFirewallBan>>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <summary>Whether a number can be a port at all.</summary>
    /// <param name="port">The candidate.</param>
    /// <returns>True for 1-65535.</returns>
    /// <remarks>
    /// Zero is excluded because it is the proto3 default of every port field on this contract, so it
    /// is what "the caller set nothing" looks like once it reaches the wire — the one value that
    /// must never be sent as though somebody chose it.
    /// </remarks>
    private static bool IsUsablePort(int port)
    {
        return port is >= LowestPort and <= HighestPort;
    }

    /// <summary>Maps the panel's protocol onto its wire counterpart.</summary>
    /// <param name="protocol">The protocol the caller named.</param>
    /// <returns>The wire value; an unknown selector becomes the unspecified value the agent refuses.</returns>
    private static Protocol ToWireProtocol(AgentFirewallProtocol protocol)
    {
        return protocol switch
        {
            AgentFirewallProtocol.Tcp => Protocol.Tcp,
            AgentFirewallProtocol.Udp => Protocol.Udp,
            _ => Protocol.Unspecified,
        };
    }

    /// <summary>Copies the host's SSH ports onto a request's repeated field.</summary>
    /// <param name="target">The request field to fill.</param>
    /// <param name="sshPorts">The ports, already checked by <see cref="RefuseUnusableHostPorts"/>.</param>
    /// <remarks>
    /// The union is sent whole and in the caller's order; nothing is deduplicated or sorted here,
    /// because the agent renders one accept per distinct port and doing it twice would let this file
    /// and the agent disagree about what "the host's ports" are.
    /// </remarks>
    private static void AddSshPorts(RepeatedField<uint> target, IReadOnlyList<int> sshPorts)
    {
        foreach (var sshPort in sshPorts)
        {
            target.Add((uint)sshPort);
        }
    }

    /// <summary>Converts a ban's duration into the whole seconds the wire carries.</summary>
    /// <param name="ttl">The duration the caller asked for, or null for a permanent ban.</param>
    /// <param name="durationSeconds">
    /// The value to send: 0 for a permanent ban, and otherwise the duration TRUNCATED to whole
    /// seconds — 90.7 seconds is sent as 90.
    /// </param>
    /// <returns>False when the duration cannot be carried at all.</returns>
    /// <remarks>
    /// The one dangerous conversion in this file, and the danger is entirely below one second. On
    /// the wire 0 means "permanent until somebody unbans it", so half a second truncated to 0, or a
    /// negative duration clamped to it, would arrive as the opposite of what the caller asked for —
    /// a ban that never ends where a brief one was wanted. That is refused, and null is the only
    /// way to ask for permanent. So is a duration beyond what the field can hold.
    ///
    /// Everything at or above one second has its fraction DROPPED, deliberately and not as an
    /// oversight: the wire has one-second resolution, a ttl computed as an expiry minus a clock
    /// reading is almost never whole, and refusing those would make the ordinary way of asking for
    /// a ban fail. Truncation rather than rounding, so an installed ban is never longer than the
    /// one asked for — and the one-second floor above is exactly what makes truncation safe here,
    /// since no value that reaches the cast can fall to 0.
    /// </remarks>
    private static bool TryToDurationSeconds(TimeSpan? ttl, out uint durationSeconds)
    {
        if (ttl is null)
        {
            durationSeconds = 0;
            return true;
        }

        var seconds = ttl.Value.TotalSeconds;
        if (seconds < 1 || seconds > uint.MaxValue)
        {
            durationSeconds = 0;
            return false;
        }

        durationSeconds = (uint)seconds;
        return true;
    }

    /// <summary>Projects the wire listing onto the panel's DTOs, or refuses a row it cannot name.</summary>
    /// <param name="ok">The success payload of <c>ListRules</c>.</param>
    /// <returns>
    /// The rules in the order the ruleset holds them, or <c>AgentInvalidResponse</c> when a row
    /// carries a port outside 1-65535 or a protocol this panel has no name for.
    /// </returns>
    /// <remarks>
    /// Such a row cannot be shown and cannot be denied: the panel would have to send the protocol
    /// back to remove the rule, and it has no value to send. Refusing the listing says so, where
    /// calling an unknown protocol TCP would offer an administrator a "deny" button that removes a
    /// different rule from the one on the screen.
    /// </remarks>
    private static Result<IReadOnlyList<AgentFirewallRule>> ToRulesResult(ListRulesOk ok)
    {
        var rules = new List<AgentFirewallRule>(ok.Rules.Count);

        foreach (var rule in ok.Rules)
        {
            if (rule.Port < LowestPort || rule.Port > HighestPort)
            {
                return Result<IReadOnlyList<AgentFirewallRule>>.Fail(
                    Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
            }

            var protocol = ToPanelProtocol(rule.Protocol);
            if (protocol is null)
            {
                return Result<IReadOnlyList<AgentFirewallRule>>.Fail(
                    Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
            }

            rules.Add(new AgentFirewallRule((int)rule.Port, protocol.Value, rule.SourceCidr));
        }

        return Result<IReadOnlyList<AgentFirewallRule>>.Ok(rules);
    }

    /// <summary>Maps a wire protocol onto the panel's own, or nothing when the panel has no name for it.</summary>
    /// <param name="protocol">The value the agent sent.</param>
    /// <returns>The panel value, or null for unspecified and for anything a newer agent adds.</returns>
    private static AgentFirewallProtocol? ToPanelProtocol(Protocol protocol)
    {
        return protocol switch
        {
            Protocol.Tcp => AgentFirewallProtocol.Tcp,
            Protocol.Udp => AgentFirewallProtocol.Udp,
            _ => null,
        };
    }

    /// <summary>Projects the wire ban listing onto the panel's DTOs.</summary>
    /// <param name="ok">The success payload of <c>ListBans</c>.</param>
    /// <returns>
    /// The bans in the order the agent sent them, each keeping an absent timeout as null rather than
    /// as a zero duration: absent is what a permanent ban looks like, and zero would read as one
    /// expiring this second.
    /// </returns>
    /// <remarks>
    /// The wire's <c>reason</c> and <c>expires_at_unix</c> are deprecated and written empty and 0;
    /// neither is read here, and the panel-side type has no member for either. The reason lives in
    /// the Firewall module's own row, and an absolute expiry would need a clock reading nobody in
    /// this path takes.
    /// </remarks>
    private static List<AgentFirewallBan> ToBans(ListBansOk ok)
    {
        var bans = new List<AgentFirewallBan>(ok.Bans.Count);

        foreach (var ban in ok.Bans)
        {
            bans.Add(new AgentFirewallBan(
                ban.Address,
                ban.HasExpiresInSeconds ? TimeSpan.FromSeconds(ban.ExpiresInSeconds) : null));
        }

        return bans;
    }

    /// <summary>Refuses a call whose host facts cannot render a ruleset the operator survives.</summary>
    /// <param name="operation">The call being refused, so the log line names it.</param>
    /// <param name="sshPorts">Every port this host's sshd listens on.</param>
    /// <param name="panelPort">The public port the panel is reachable on.</param>
    /// <returns>The error to return, or null when the facts are usable.</returns>
    /// <remarks>
    /// The code is <c>AgentFirewallPortsMisconfigured</c> and NOT the <c>AgentInvalidInput</c> that
    /// <see cref="RefuseUnusableRulePort"/> returns, because the two failures have different owners
    /// and different remedies. A bad rule port is a bad request: somebody typed it, and retyping it
    /// fixes it. Unusable host facts mean this panel does not know which ports must stay open —
    /// the installer writes them into <c>panel.env</c> and the module validates them at startup, so
    /// one arriving unusable here is a broken deployment that no amount of retyping will fix, and a
    /// caller that could not tell the two apart would answer both with "check your details".
    ///
    /// Which fact is unusable is operator-facing detail with no resource entry, so it goes to the
    /// log below rather than onto the <see cref="Error"/>, which carries a code and nothing else
    /// (rules/csharp.md).
    /// </remarks>
    private Error? RefuseUnusableHostPorts(string operation, IReadOnlyList<int> sshPorts, int panelPort)
    {
        if (sshPorts.Count == 0)
        {
            LogRefusedHostFact(_logger, operation, "no ssh port was supplied", null);
            return Error.Of(nameof(ErrorMessages.AgentFirewallPortsMisconfigured), ErrorType.Failure);
        }

        foreach (var sshPort in sshPorts)
        {
            if (!IsUsablePort(sshPort))
            {
                LogRefusedHostFact(
                    _logger,
                    operation,
                    FormattableString.Invariant($"the ssh port {sshPort} is outside 1-65535"),
                    null);

                return Error.Of(nameof(ErrorMessages.AgentFirewallPortsMisconfigured), ErrorType.Failure);
            }
        }

        if (!IsUsablePort(panelPort))
        {
            LogRefusedHostFact(
                _logger,
                operation,
                FormattableString.Invariant($"the panel port {panelPort} is outside 1-65535"),
                null);

            return Error.Of(nameof(ErrorMessages.AgentFirewallPortsMisconfigured), ErrorType.Failure);
        }

        return null;
    }

    /// <summary>Refuses a rule whose own port is not a port.</summary>
    /// <param name="operation">The call being refused, so the log line names it.</param>
    /// <param name="port">The port the caller asked to allow or deny.</param>
    /// <returns>The error to return, or null when the port is usable.</returns>
    /// <remarks>
    /// Checked here rather than left to the agent for one reason: the wire field is unsigned, so a
    /// negative number does not arrive as a refusal, it arrives as a port in the billions that
    /// nobody typed. The agent refuses that too, but its diagnostic then names a number the operator
    /// never entered.
    /// </remarks>
    private Error? RefuseUnusableRulePort(string operation, int port)
    {
        if (IsUsablePort(port))
        {
            return null;
        }

        LogRefusedArgument(
            _logger,
            operation,
            FormattableString.Invariant($"the port {port} is outside 1-65535"),
            null);

        return Error.Of(nameof(ErrorMessages.AgentInvalidInput), ErrorType.Validation);
    }
}
