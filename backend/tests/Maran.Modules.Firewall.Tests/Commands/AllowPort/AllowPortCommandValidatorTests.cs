using Maran.Agent.Client.Services.FirewallService;
using Maran.Modules.Firewall.Commands.AllowPort;

namespace Maran.Modules.Firewall.Tests.Commands.AllowPort;

/// <summary>What a rule request has to look like before a handler ever sees it.</summary>
public sealed class AllowPortCommandValidatorTests
{
    /// <summary>A source range with host bits beyond its prefix is refused rather than masked.</summary>
    [Theory]
    [InlineData("203.0.113.7/24")]
    [InlineData("10.0.0.1/8")]
    public void A_source_range_with_host_bits_beyond_its_prefix_is_refused_rather_than_masked(string sourceCidr)
    {
        // The two readings of 203.0.113.7/24 — one host or two hundred and fifty-six of them —
        // differ by the whole blast radius of the rule. Masking silently picks one; refusing makes
        // the administrator say which they meant.
        var result = Validate(8080, sourceCidr);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == "RuleSourceCidrInvalid";
        });
    }

    /// <summary>A source range that is not a range at all is refused.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("203.0.113.7")]
    [InlineData("everything")]
    public void A_source_range_that_is_not_a_range_at_all_is_refused(string sourceCidr)
    {
        Assert.False(Validate(8080, sourceCidr).IsValid);
    }

    /// <summary>A port outside one to sixty five thousand five hundred and thirty five is refused.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void A_port_outside_one_to_sixty_five_thousand_five_hundred_and_thirty_five_is_refused(int port)
    {
        // Zero especially: it is the proto3 default of every port field on the agent contract, so it
        // is what "nobody set this" looks like once it reaches the wire.
        var result = Validate(port, "0.0.0.0/0");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == "RulePortInvalid";
        });
    }

    /// <summary>An any source rule on a real port is accepted.</summary>
    [Fact]
    public void An_any_source_rule_on_a_real_port_is_accepted()
    {
        // Guards every refusal above from passing for the wrong reason.
        Assert.True(Validate(8080, "0.0.0.0/0").IsValid);
    }

    /// <summary>An any source ipv6 rule is accepted.</summary>
    [Fact]
    public void An_any_source_ipv6_rule_is_accepted()
    {
        Assert.True(Validate(8080, "::/0").IsValid);
    }

    /// <summary>A range that does not end above where it starts is refused.</summary>
    [Theory]
    [InlineData(30_000)]
    [InlineData(29_999)]
    [InlineData(0)]
    [InlineData(70_000)]
    public void A_range_that_does_not_end_above_where_it_starts_is_refused(int portTo)
    {
        // Equal bounds are in this list because `nft` ACCEPTS them: they would become a second
        // spelling of the single port 30000, and a later deny naming that port would match nothing
        // and report success while the port stayed open.
        var result = Validate(30_000, "0.0.0.0/0", portTo);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
        {
            return error.ErrorMessage == "RulePortRangeInvalid";
        });
    }

    /// <summary>A range that ends above where it starts is accepted, and so is no range at all.</summary>
    [Fact]
    public void A_range_that_ends_above_where_it_starts_is_accepted_and_so_is_no_range_at_all()
    {
        // The inverse control (rules/testing.md): a rule that refused everything would pass every
        // test above and nothing here.
        Assert.True(Validate(30_000, "0.0.0.0/0", 30_099).IsValid);
        Assert.True(Validate(30_000, "0.0.0.0/0").IsValid);
    }

    /// <summary>Runs the validator over one candidate rule.</summary>
    /// <param name="port">The port the rule names.</param>
    /// <param name="sourceCidr">The source range it is scoped to.</param>
    /// <param name="portTo">The range's upper bound, or null for a single port.</param>
    private static FluentValidation.Results.ValidationResult Validate(
        int port,
        string sourceCidr,
        int? portTo = null)
    {
        return new AllowPortCommandValidator().Validate(
            new AllowPortCommand(port, AgentFirewallProtocol.Tcp, sourceCidr, portTo, "198.51.100.1", "curl"));
    }
}
