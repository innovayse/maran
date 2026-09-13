using Maran.Modules.Ftp.Commands.EnableFtps;
using Maran.Modules.Ftp.Domain.Policies;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The panel's half of the FTPS seam: the exact shape this panel puts on the wire when an operator
/// enables the daemon, pinned against the literals the agent's own seam test accepts.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this file exists.</b> Two verification passes each walked the FTPS customer path and each
/// was honest about its own layer, and comparing them showed what neither could see alone: the
/// panel's half is proven against <see cref="Fixtures.StubAgentFtpsClient"/>, which accepts
/// everything and records nothing, and the daemon's half is proven against <c>ops::ftps</c> called
/// directly on a real host. So nothing observed whether the shape the panel SENDS is a shape the
/// agent ACCEPTS — and that is exactly where this branch's most expensive FTPS defect lived: an
/// optional passive address spelled <c>null</c> on one side and <c>""</c> on the other, which would
/// have made FTPS unswitchable on every host that is not behind NAT, invisible to both layers
/// separately because the panel's stub accepted what the agent would have refused.
/// </para>
/// <para>
/// <b>What this file can and cannot reach.</b> It reaches the panel's outgoing shape and nothing
/// else. It starts no host, opens no database and speaks to no agent: the values here are the
/// panel's own constants and its own validator. The agent's half of the same pin is
/// <c>the_enable_shape_the_panel_sends_passes_the_agents_input_boundary_and_a_wrong_family_passive_address_does_not</c>
/// in <c>agent/crates/agent/tests/handshake.rs</c>, which drives the real agent server over a real
/// unix socket with these same five values. Neither language can read the other, so the agreement is
/// held by two sets of literals that name each other: moving one turns the other's test red and the
/// reader is told where the twin is. <b>UNOBSERVED HERE: the daemon, and the round trip.</b> That
/// a daemon configured from this shape then answers a customer is
/// <c>agent/crates/agent/tests/ftps_on_a_real_host.rs</c>; that the handler really reads these
/// constants is <c>EnableFtpsCommandHandlerTests</c>; that the client really copies them onto the
/// wire is <c>AgentFtpsClientTests</c>.
/// </para>
/// <para>
/// It lives in the integration project rather than beside the module's unit tests because the seam
/// it pins is not the module's — it is the boundary between two processes written in two languages,
/// which is the one thing this project exists to observe.
/// </para>
/// </remarks>
public sealed class PanelToAgentFtpsWireShapeTests
{
    /// <summary>The lowest passive port the agent's seam test carries as the panel's own value.</summary>
    private const uint PassivePortMinTheAgentAccepts = 30_000;

    /// <summary>The highest passive port the agent's seam test carries as the panel's own value.</summary>
    private const uint PassivePortMaxTheAgentAccepts = 30_099;

    /// <summary>The session ceiling the agent's seam test carries as the panel's own value.</summary>
    private const uint MaxClientsTheAgentAccepts = 100;

    /// <summary>The validator the enable endpoint runs before the handler; it holds no state.</summary>
    private readonly EnableFtpsCommandValidator _validator = new();

    /// <summary>The passive range and the ceiling the panel sends are the literals the agent accepts.</summary>
    /// <remarks>
    /// Compared against literals on purpose, not against the constants themselves, which would make
    /// this test agree with any value <see cref="FtpsDefaults"/> was changed to while the agent's
    /// twin still carried the old one. A change to these numbers is meant to be red here, and the
    /// message says which file the other half is in.
    /// </remarks>
    [Fact]
    public void The_passive_range_and_ceiling_the_panel_sends_are_the_numbers_the_agent_accepts()
    {
        Assert.Equal(PassivePortMinTheAgentAccepts, (uint)FtpsDefaults.PassivePortMin);
        Assert.Equal(PassivePortMaxTheAgentAccepts, (uint)FtpsDefaults.PassivePortMax);
        Assert.Equal(MaxClientsTheAgentAccepts, (uint)FtpsDefaults.MaxClients);
    }

    /// <summary>An operator who gives no passive address sends the empty string the agent reads as absence.</summary>
    /// <remarks>
    /// The two halves of one fact, because either alone is satisfiable while FTPS is unswitchable:
    /// the command's default is the EMPTY STRING, which is the one spelling of absence the agent
    /// contract defines, and the validator ACCEPTS it rather than judging it as an address. The
    /// agent's twin asserts the other side — that an empty <c>passive_address</c> passes its input
    /// boundary and is refused only for missing certificate material.
    /// </remarks>
    [Fact]
    public void An_operator_who_gives_no_passive_address_sends_the_empty_string_the_agent_reads_as_absence()
    {
        var command = new EnableFtpsCommand("ftp.example.test");

        Assert.Equal(string.Empty, command.PassiveAddress);
        Assert.True(_validator.Validate(command).IsValid);
    }

    /// <summary>The panel refuses an IPv6 passive address, as the agent does.</summary>
    /// <remarks>
    /// <para>
    /// <b>A disagreement that used to be pinned rather than endorsed, now closed.</b> This validator
    /// admitted anything <c>IPAddress.TryParse</c> parses, IPv6 included, while the agent's
    /// <c>PassiveAddress</c> refuses the wrong family through its own error variant —
    /// <c>pasv_address</c> is an IPv4-only directive, since the PASV reply carries four decimal
    /// octets and has no IPv6 form. So an operator who typed an IPv6 literal was accepted at the
    /// form and refused one process later, which reached them as a server failure instead of the 400
    /// that names the field.
    /// </para>
    /// <para>
    /// The panel is the side that moved: the daemon's directive is IPv4-only and is not this
    /// panel's to widen. The agent's twin asserts the refusal of this same literal at its own input
    /// boundary, so the two tests together now say that both ends refuse it rather than where they
    /// disagreed. <b>UNOBSERVED HERE: the sentence the operator reads</b>, which is the module's
    /// <c>ErrorMessagesTests</c>; this observes only that the panel refuses, and refuses with the
    /// code whose sentence names IPv4.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_panel_refuses_the_ipv6_passive_address_the_agent_refuses()
    {
        var command = new EnableFtpsCommand("ftp.example.test", "2001:db8::1");

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == "FtpsPassiveAddressInvalid";
        });
    }

    /// <summary>The panel still accepts the IPv4 passive address the agent accepts.</summary>
    /// <remarks>
    /// The inverse control the refusal above owes. A validator narrowed until it refuses every
    /// passive address would satisfy that test and make FTPS unswitchable for every host behind NAT
    /// — the mirror image of the defect this file was written for, where refusing the EMPTY string
    /// made it unswitchable for every host that is not. Both halves are asserted here because both
    /// have to survive, and the literal is the one the agent's twin carries.
    /// </remarks>
    [Fact]
    public void The_panel_still_accepts_the_ipv4_passive_address_the_agent_accepts()
    {
        var command = new EnableFtpsCommand("ftp.example.test", "203.0.113.7");

        Assert.True(_validator.Validate(command).IsValid);
    }
}
