using Maran.Agent.Client.Services.FirewallService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.FirewallService;

/// <summary>Mapping contract of AgentFirewallClient, and the host facts it refuses to send without.</summary>
public sealed class AgentFirewallClientTests
{
    /// <summary>Any source, the value the panel sends for an unrestricted rule.</summary>
    private const string AnySource = "0.0.0.0/0";

    /// <summary>The panel's public port in every test that sends one.</summary>
    private const int PanelPort = 8443;

    /// <summary>The ports this imaginary host's sshd listens on: two of them, as a real host may.</summary>
    private static readonly int[] SshPorts = [22, 2222];

    /// <summary>Listing sends every ssh port and the panel port so the rendered accepts can be told apart.</summary>
    /// <remarks>
    /// A read carries them because the ruleset's unconditional accepts are byte-identical to an
    /// operator's own any-source TCP allow. A listing that did not know the ports would report the
    /// panel's own accept as a rule somebody created, and an administrator would try to deny it.
    /// </remarks>
    [Fact]
    public async Task Listing_sends_every_ssh_port_and_the_panel_port_so_the_rendered_accepts_can_be_told_apart()
    {
        var stub = new StubFirewallService
        {
            ListRulesResponse = new ListRulesResponse { Ok = new ListRulesOk() },
        };

        await Client(stub).ListRulesAsync(SshPorts, PanelPort, CancellationToken.None);

        var request = Assert.IsType<ListRulesRequest>(stub.LastListRulesRequest);
        Assert.Equal(new uint[] { 22, 2222 }, request.SshPorts);
        Assert.Equal(8443u, request.PanelPort);
    }

    /// <summary>Listing ok payload maps the port the protocol and the source of every rule.</summary>
    [Fact]
    public async Task Listing_ok_payload_maps_the_port_the_protocol_and_the_source_of_every_rule()
    {
        var stub = new StubFirewallService
        {
            ListRulesResponse = new ListRulesResponse
            {
                Ok = new ListRulesOk
                {
                    Rules =
                    {
                        new FirewallRule { Port = 443, Protocol = Protocol.Tcp, SourceCidr = AnySource },
                        new FirewallRule { Port = 53, Protocol = Protocol.Udp, SourceCidr = "10.0.0.0/8" },
                    },
                },
            },
        };

        var result = await Client(stub).ListRulesAsync(SshPorts, PanelPort, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new AgentFirewallRule(443, AgentFirewallProtocol.Tcp, AnySource), result.Value[0]);
        Assert.Equal(new AgentFirewallRule(53, AgentFirewallProtocol.Udp, "10.0.0.0/8"), result.Value[1]);
    }

    /// <summary>A rule whose protocol this panel cannot name is refused rather than shown as tcp.</summary>
    /// <remarks>
    /// The panel has to send the protocol back to remove a rule, so a row it cannot name is a row it
    /// cannot deny. Calling it TCP would offer an administrator a button that removes a different
    /// rule from the one on the screen.
    /// </remarks>
    [Fact]
    public async Task A_rule_whose_protocol_this_panel_cannot_name_is_refused_rather_than_shown_as_tcp()
    {
        var stub = new StubFirewallService
        {
            ListRulesResponse = new ListRulesResponse
            {
                Ok = new ListRulesOk
                {
                    Rules = { new FirewallRule { Port = 443, SourceCidr = AnySource } },
                },
            },
        };

        var result = await Client(stub).ListRulesAsync(SshPorts, PanelPort, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>A rule whose port is not a port is refused rather than wrapped into one.</summary>
    [Fact]
    public async Task A_rule_whose_port_is_not_a_port_is_refused_rather_than_wrapped_into_one()
    {
        var stub = new StubFirewallService
        {
            ListRulesResponse = new ListRulesResponse
            {
                Ok = new ListRulesOk
                {
                    Rules =
                    {
                        new FirewallRule { Port = 70_000, Protocol = Protocol.Tcp, SourceCidr = AnySource },
                    },
                },
            },
        };

        var result = await Client(stub).ListRulesAsync(SshPorts, PanelPort, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Listing error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task Listing_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubFirewallService
        {
            ListRulesResponse = new ListRulesResponse
            {
                Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "ruleset unreadable" },
            },
        };

        var result = await Client(stub).ListRulesAsync(SshPorts, PanelPort, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    /// <summary>A rule listing with neither branch set is refused rather than read as empty.</summary>
    [Fact]
    public async Task A_rule_listing_with_neither_branch_set_is_refused_rather_than_read_as_empty()
    {
        var result = await Client(new StubFirewallService())
            .ListRulesAsync(SshPorts, PanelPort, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>A listing with no ssh port is refused here and never sent.</summary>
    /// <remarks>
    /// A read cannot lock anybody out, so this refusal buys no safety of its own; it is here so that
    /// these two fields mean one thing in every message that carries them. The value of that is
    /// exactly what the mutation shows: the reading and the writing paths share the check, so a
    /// caller cannot learn on the listing that an empty list is tolerated.
    /// </remarks>
    [Fact]
    public async Task A_listing_with_no_ssh_port_is_refused_here_and_never_sent()
    {
        var stub = new StubFirewallService();

        var result = await Client(stub).ListRulesAsync([], PanelPort, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentFirewallPortsMisconfigured", result.Error!.Code);
        Assert.Null(stub.LastListRulesRequest);
    }

    /// <summary>Allowing a port sends the rule the ssh ports and the panel port together.</summary>
    [Fact]
    public async Task Allowing_a_port_sends_the_rule_the_ssh_ports_and_the_panel_port_together()
    {
        var stub = StubFirewallService.AcceptingAllow();

        await Client(stub).AllowPortAsync(
            443,
            AgentFirewallProtocol.Tcp,
            AnySource,
            SshPorts,
            PanelPort,
            CancellationToken.None);

        var request = Assert.IsType<AllowPortRequest>(stub.LastAllowRequest);
        Assert.Equal(443u, request.Port);
        Assert.Equal(Protocol.Tcp, request.Protocol);
        Assert.Equal(AnySource, request.SourceCidr);
        Assert.Equal(new uint[] { 22, 2222 }, request.SshPorts);
        Assert.Equal(8443u, request.PanelPort);
    }

    /// <summary>A udp allow reaches the wire as udp.</summary>
    [Fact]
    public async Task A_udp_allow_reaches_the_wire_as_udp()
    {
        var stub = StubFirewallService.AcceptingAllow();

        await Client(stub).AllowPortAsync(
            53,
            AgentFirewallProtocol.Udp,
            AnySource,
            SshPorts,
            PanelPort,
            CancellationToken.None);

        Assert.Equal(Protocol.Udp, stub.LastAllowRequest!.Protocol);
    }

    /// <summary>An allow with no ssh port is refused here and never sent.</summary>
    /// <remarks>
    /// The one that matters. The agent re-renders the whole ruleset on this call under a drop policy,
    /// so a request carrying no ssh port — or a client that quietly substituted 22 for the missing
    /// list — closes the operator's own session on a host whose sshd listens elsewhere, and there is
    /// no remote way back in. The installer detects the real ports and writes them into panel.env;
    /// an empty list arriving here means that broke, and the honest answer is to change nothing.
    /// </remarks>
    [Fact]
    public async Task An_allow_with_no_ssh_port_is_refused_here_and_never_sent()
    {
        var stub = StubFirewallService.AcceptingAllow();

        var result = await Client(stub).AllowPortAsync(
            443,
            AgentFirewallProtocol.Tcp,
            AnySource,
            [],
            PanelPort,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentFirewallPortsMisconfigured", result.Error!.Code);
        Assert.Null(stub.LastAllowRequest);
    }

    /// <summary>The refusal of a missing host fact names it in the log where an operator will read it.</summary>
    /// <remarks>
    /// The returned error carries a code and nothing else, by design, so the diagnosis has to be
    /// somewhere: without this line the operator of a panel whose ssh ports failed to reach it sees
    /// only "some of the details you submitted were not accepted" for a value they never submitted.
    /// </remarks>
    [Fact]
    public async Task The_refusal_of_a_missing_host_fact_names_it_in_the_log_where_an_operator_will_read_it()
    {
        var logger = new RecordingLogger<AgentFirewallClient>();

        await new AgentFirewallClient(StubFirewallService.AcceptingAllow(), logger).AllowPortAsync(
            443,
            AgentFirewallProtocol.Tcp,
            AnySource,
            [],
            PanelPort,
            CancellationToken.None);

        var logged = Assert.Single(logger.Messages);
        Assert.Contains("AllowPortAsync", logged, StringComparison.Ordinal);
        Assert.Contains("no ssh port was supplied", logged, StringComparison.Ordinal);
    }

    /// <summary>An allow whose panel port is zero is refused here and never sent.</summary>
    /// <remarks>
    /// Zero is the proto3 default of the field, so it is exactly what "nobody set this" looks like by
    /// the time it reaches the wire — and a ruleset rendered without the panel's port closes the
    /// panel to the world under the same drop policy.
    /// </remarks>
    [Fact]
    public async Task An_allow_whose_panel_port_is_zero_is_refused_here_and_never_sent()
    {
        var stub = StubFirewallService.AcceptingAllow();

        var result = await Client(stub).AllowPortAsync(
            443,
            AgentFirewallProtocol.Tcp,
            AnySource,
            SshPorts,
            0,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentFirewallPortsMisconfigured", result.Error!.Code);
        Assert.Null(stub.LastAllowRequest);
    }

    /// <summary>An allow naming an ssh port outside the range is refused here and never sent.</summary>
    [Fact]
    public async Task An_allow_naming_an_ssh_port_outside_the_range_is_refused_here_and_never_sent()
    {
        var stub = StubFirewallService.AcceptingAllow();

        var result = await Client(stub).AllowPortAsync(
            443,
            AgentFirewallProtocol.Tcp,
            AnySource,
            [22, 70_000],
            PanelPort,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentFirewallPortsMisconfigured", result.Error!.Code);
        Assert.Null(stub.LastAllowRequest);
    }

    /// <summary>A rule port that is not a port is refused before it becomes an unsigned number nobody typed.</summary>
    /// <param name="port">The unusable port the caller asked for.</param>
    /// <remarks>
    /// The wire field is unsigned, so a negative number does not arrive at the agent as a refusal —
    /// it arrives as a port in the billions, and the agent's diagnostic then names a number the
    /// operator never entered.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70_000)]
    public async Task A_rule_port_that_is_not_a_port_is_refused_before_it_becomes_an_unsigned_number(int port)
    {
        var stub = StubFirewallService.AcceptingAllow();

        var result = await Client(stub).AllowPortAsync(
            port,
            AgentFirewallProtocol.Tcp,
            AnySource,
            SshPorts,
            PanelPort,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidInput", result.Error!.Code);
        Assert.Null(stub.LastAllowRequest);
    }

    /// <summary>A broken host fact and a bad rule port are told apart by their codes.</summary>
    /// <remarks>
    /// Both refusals leave the host's firewall untouched, and a caller that saw one code for both
    /// would have to answer them the same way. They are not the same: a bad rule port is fixed by
    /// retyping it, and unusable host facts are fixed by repairing panel.env, which no administrator
    /// can do from the screen that asked for the rule. Asserted as an inequality as well as by name,
    /// so collapsing the two back into one code fails here rather than only in whichever module
    /// happens to branch on it.
    /// </remarks>
    [Fact]
    public async Task A_broken_host_fact_and_a_bad_rule_port_are_told_apart_by_their_codes()
    {
        var brokenHostFact = await Client(StubFirewallService.AcceptingAllow()).AllowPortAsync(
            443,
            AgentFirewallProtocol.Tcp,
            AnySource,
            [],
            PanelPort,
            CancellationToken.None);
        var badRulePort = await Client(StubFirewallService.AcceptingAllow()).AllowPortAsync(
            0,
            AgentFirewallProtocol.Tcp,
            AnySource,
            SshPorts,
            PanelPort,
            CancellationToken.None);

        Assert.Equal("AgentFirewallPortsMisconfigured", brokenHostFact.Error!.Code);
        Assert.Equal("AgentInvalidInput", badRulePort.Error!.Code);
        Assert.NotEqual(brokenHostFact.Error.Code, badRulePort.Error.Code);
    }

    /// <summary>The allow ok payload maps to success.</summary>
    [Fact]
    public async Task The_allow_ok_payload_maps_to_success()
    {
        var result = await AllowAsync(StubFirewallService.AcceptingAllow());

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    /// <summary>The allow error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task The_allow_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = StubFirewallService.FailingAllowWith(ErrorCode.AlreadyExists, "rule exists");

        var result = await AllowAsync(stub);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentAlreadyExists", result.Error!.Code);
    }

    /// <summary>An allow response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task An_allow_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await AllowAsync(new StubFirewallService());

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>The agents diagnostic text is logged and never carried back to the caller.</summary>
    [Fact]
    public async Task The_agents_diagnostic_text_is_logged_and_never_carried_back_to_the_caller()
    {
        var logger = new RecordingLogger<AgentFirewallClient>();
        var stub = StubFirewallService.FailingAllowWith(
            ErrorCode.SystemFailure,
            "nft: /etc/maran/nftables.conf line 12 failed");

        var result = await new AgentFirewallClient(stub, logger).AllowPortAsync(
            443,
            AgentFirewallProtocol.Tcp,
            AnySource,
            SshPorts,
            PanelPort,
            CancellationToken.None);

        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.DoesNotContain("/etc/maran", result.Error.Code, StringComparison.Ordinal);
        var logged = Assert.Single(logger.Messages);
        Assert.Contains("/etc/maran/nftables.conf line 12 failed", logged, StringComparison.Ordinal);
    }

    /// <summary>Denying a port sends the rule the ssh ports and the panel port together.</summary>
    [Fact]
    public async Task Denying_a_port_sends_the_rule_the_ssh_ports_and_the_panel_port_together()
    {
        var stub = StubFirewallService.AcceptingDeny();

        await Client(stub).DenyPortAsync(
            3306,
            AgentFirewallProtocol.Tcp,
            "10.0.0.0/8",
            SshPorts,
            PanelPort,
            CancellationToken.None);

        var request = Assert.IsType<DenyPortRequest>(stub.LastDenyRequest);
        Assert.Equal(3306u, request.Port);
        Assert.Equal(Protocol.Tcp, request.Protocol);
        Assert.Equal("10.0.0.0/8", request.SourceCidr);
        Assert.Equal(new uint[] { 22, 2222 }, request.SshPorts);
        Assert.Equal(8443u, request.PanelPort);
    }

    /// <summary>A deny with no ssh port is refused here and never sent.</summary>
    /// <remarks>
    /// A deny re-renders the whole ruleset exactly as an allow does, so it locks an operator out just
    /// as thoroughly. The check belongs on both, and each is asserted on its own rather than one
    /// standing for the pair.
    /// </remarks>
    [Fact]
    public async Task A_deny_with_no_ssh_port_is_refused_here_and_never_sent()
    {
        var stub = StubFirewallService.AcceptingDeny();

        var result = await Client(stub).DenyPortAsync(
            3306,
            AgentFirewallProtocol.Tcp,
            AnySource,
            [],
            PanelPort,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentFirewallPortsMisconfigured", result.Error!.Code);
        Assert.Null(stub.LastDenyRequest);
    }

    /// <summary>A deny whose panel port is zero is refused here and never sent.</summary>
    [Fact]
    public async Task A_deny_whose_panel_port_is_zero_is_refused_here_and_never_sent()
    {
        var stub = StubFirewallService.AcceptingDeny();

        var result = await Client(stub).DenyPortAsync(
            3306,
            AgentFirewallProtocol.Tcp,
            AnySource,
            SshPorts,
            0,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentFirewallPortsMisconfigured", result.Error!.Code);
        Assert.Null(stub.LastDenyRequest);
    }

    /// <summary>The deny ok payload maps to success.</summary>
    [Fact]
    public async Task The_deny_ok_payload_maps_to_success()
    {
        var result = await DenyAsync(StubFirewallService.AcceptingDeny());

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    /// <summary>The deny error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task The_deny_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubFirewallService
        {
            DenyResponse = new DenyPortResponse
            {
                Error = new AgentError { Code = ErrorCode.InvalidInput, Message = "bad cidr" },
            },
        };

        var result = await DenyAsync(stub);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidInput", result.Error!.Code);
    }

    /// <summary>A deny response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task A_deny_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await DenyAsync(new StubFirewallService());

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>A ban with a duration sends whole seconds and no reason.</summary>
    /// <remarks>
    /// The reason is deliberately absent from the wire: the agent stores none, because the only place
    /// one could go there is an nftables comment, whose argument nft parses in its own grammar.
    /// </remarks>
    [Fact]
    public async Task A_ban_with_a_duration_sends_whole_seconds_and_no_reason()
    {
        var stub = StubFirewallService.AcceptingBan();

        await Client(stub).BanAsync("203.0.113.7", TimeSpan.FromHours(1), CancellationToken.None);

        var request = Assert.IsType<BanAddressRequest>(stub.LastBanRequest);
        Assert.Equal("203.0.113.7", request.Address);
        Assert.Equal(3600u, request.DurationSeconds);
        Assert.Equal(string.Empty, request.Reason);
    }

    /// <summary>A ban with no duration is sent as the zero the contract spells permanent.</summary>
    [Fact]
    public async Task A_ban_with_no_duration_is_sent_as_the_zero_the_contract_spells_permanent()
    {
        var stub = StubFirewallService.AcceptingBan();

        var result = await Client(stub).BanAsync("203.0.113.7", null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0u, stub.LastBanRequest!.DurationSeconds);
    }

    /// <summary>A duration the wire cannot carry is refused rather than rounded into a permanent ban.</summary>
    /// <param name="milliseconds">The duration the caller asked for.</param>
    /// <remarks>
    /// Zero seconds means permanent on this contract, so truncating half a second — or clamping a
    /// negative — would turn a brief ban into one that never ends and that nobody remembers placing.
    /// Null is the only way to ask for permanent.
    /// </remarks>
    [Theory]
    [InlineData(500)]
    [InlineData(0)]
    [InlineData(-1000)]
    public async Task A_duration_the_wire_cannot_carry_is_refused_rather_than_rounded(int milliseconds)
    {
        var stub = StubFirewallService.AcceptingBan();

        var result = await Client(stub)
            .BanAsync("203.0.113.7", TimeSpan.FromMilliseconds(milliseconds), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidInput", result.Error!.Code);
        Assert.Null(stub.LastBanRequest);
    }

    /// <summary>A fractional duration at or above a second is truncated and never rounded up to a longer ban.</summary>
    /// <param name="milliseconds">The duration the caller asked for.</param>
    /// <param name="expectedSeconds">The whole seconds that must reach the wire.</param>
    /// <remarks>
    /// Declared behaviour rather than an accident of the cast: a ttl computed as an expiry minus a
    /// clock reading is almost never whole, so refusing every fraction would make the ordinary way
    /// of asking for a ban fail. Truncation and not rounding, so an installed ban is never longer
    /// than the one asked for — and the second row is the one that matters, because it proves the
    /// under-one-second floor is what keeps truncation away from the 0 that means permanent.
    /// </remarks>
    [Theory]
    [InlineData(90_700, 90u)]
    [InlineData(1_900, 1u)]
    public async Task A_fractional_duration_is_truncated_and_never_rounded_up(int milliseconds, uint expectedSeconds)
    {
        var stub = StubFirewallService.AcceptingBan();

        var result = await Client(stub)
            .BanAsync("203.0.113.7", TimeSpan.FromMilliseconds(milliseconds), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedSeconds, stub.LastBanRequest!.DurationSeconds);
        Assert.NotEqual(0u, stub.LastBanRequest.DurationSeconds);
    }

    /// <summary>The ban error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task The_ban_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubFirewallService
        {
            BanResponse = new BanAddressResponse
            {
                Error = new AgentError { Code = ErrorCode.InvalidInput, Message = "not an address" },
            },
        };

        var result = await Client(stub).BanAsync("nonsense", null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidInput", result.Error!.Code);
    }

    /// <summary>A ban response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task A_ban_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await Client(new StubFirewallService()).BanAsync("203.0.113.7", null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Unbanning sends the address and nothing else.</summary>
    [Fact]
    public async Task Unbanning_sends_the_address_and_nothing_else()
    {
        var stub = new StubFirewallService
        {
            UnbanResponse = new UnbanAddressResponse { Ok = new UnbanAddressOk() },
        };

        var result = await Client(stub).UnbanAsync("203.0.113.7", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.IsType<UnbanAddressRequest>(stub.LastUnbanRequest);
        Assert.Equal("203.0.113.7", request.Address);
    }

    /// <summary>The unban error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task The_unban_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubFirewallService
        {
            UnbanResponse = new UnbanAddressResponse
            {
                Error = new AgentError { Code = ErrorCode.NotFound, Message = "no active ban" },
            },
        };

        var result = await Client(stub).UnbanAsync("203.0.113.7", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
    }

    /// <summary>An unban response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task An_unban_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await Client(new StubFirewallService()).UnbanAsync("203.0.113.7", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>A ban with no timeout arrives as no expiry at all and never as one expiring now.</summary>
    /// <remarks>
    /// Absent is what permanent looks like on this contract. A zero duration would read as a ban
    /// about to lapse, and the panel reconciles those two in opposite directions.
    /// </remarks>
    [Fact]
    public async Task A_ban_with_no_timeout_arrives_as_no_expiry_at_all_and_never_as_one_expiring_now()
    {
        var stub = new StubFirewallService
        {
            ListBansResponse = new ListBansResponse
            {
                Ok = new ListBansOk
                {
                    Bans =
                    {
                        new BanEntry { Address = "203.0.113.7" },
                        new BanEntry { Address = "198.51.100.4", ExpiresInSeconds = 900 },
                    },
                },
            },
        };

        var result = await Client(stub).ListBansAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new AgentFirewallBan("203.0.113.7", null), result.Value[0]);
        Assert.Equal(new AgentFirewallBan("198.51.100.4", TimeSpan.FromMinutes(15)), result.Value[1]);
        Assert.NotNull(stub.LastListBansRequest);
    }

    /// <summary>The ban listing error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task The_ban_listing_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubFirewallService
        {
            ListBansResponse = new ListBansResponse
            {
                Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "nft set unreadable" },
            },
        };

        var result = await Client(stub).ListBansAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    /// <summary>A ban listing with neither branch set is refused rather than read as no bans.</summary>
    [Fact]
    public async Task A_ban_listing_with_neither_branch_set_is_refused_rather_than_read_as_no_bans()
    {
        var result = await Client(new StubFirewallService()).ListBansAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Builds the production client over a stub transport and a logger nothing asserts.</summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <returns>The client under test.</returns>
    private static AgentFirewallClient Client(StubFirewallService stub)
    {
        return new AgentFirewallClient(stub, NullLogger<AgentFirewallClient>.Instance);
    }

    /// <summary>Calls the production allow path with fixed, valid arguments.</summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <returns>What the client returned.</returns>
    private static async Task<SharedKernel.Results.Result<bool>> AllowAsync(StubFirewallService stub)
    {
        return await Client(stub).AllowPortAsync(
            443,
            AgentFirewallProtocol.Tcp,
            AnySource,
            SshPorts,
            PanelPort,
            CancellationToken.None);
    }

    /// <summary>Calls the production deny path with fixed, valid arguments.</summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <returns>What the client returned.</returns>
    private static async Task<SharedKernel.Results.Result<bool>> DenyAsync(StubFirewallService stub)
    {
        return await Client(stub).DenyPortAsync(
            3306,
            AgentFirewallProtocol.Tcp,
            AnySource,
            SshPorts,
            PanelPort,
            CancellationToken.None);
    }
}
