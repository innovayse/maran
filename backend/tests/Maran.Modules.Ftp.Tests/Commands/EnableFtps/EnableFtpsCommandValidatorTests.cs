using Maran.Modules.Ftp.Commands.EnableFtps;
using Maran.Modules.Ftp.Tests.TestSupport;

namespace Maran.Modules.Ftp.Tests.Commands.EnableFtps;

/// <summary>Behaviour of the validator that guards the two values an operator supplies.</summary>
public sealed class EnableFtpsCommandValidatorTests
{
    /// <summary>The validator under test; it holds no state, so one instance serves every case.</summary>
    private readonly EnableFtpsCommandValidator _validator = new();

    /// <summary>A well formed hostname with no passive address is accepted.</summary>
    /// <remarks>
    /// The inverse control. A validator mutated to refuse everything passes every test that only
    /// ever hands it broken input (rules/testing.md).
    /// </remarks>
    [Fact]
    public void A_well_formed_hostname_with_no_passive_address_is_accepted()
    {
        var result = _validator.Validate(new EnableFtpsCommand("ftp.example.test"));

        Assert.True(result.IsValid);
    }

    /// <summary>A hostname carrying a trailing newline is refused.</summary>
    /// <remarks>
    /// The value is written into <c>vsftpd.conf</c>, which is line-oriented, so one embedded newline
    /// turns a single directive into several — and the directive worth appending is
    /// <c>force_local_logins_ssl=NO</c>. .NET's <c>$</c> anchor matches before a trailing newline,
    /// which is why the rule this delegates to is anchored with <c>\z</c>.
    /// </remarks>
    [Fact]
    public void A_hostname_carrying_a_trailing_newline_is_refused()
    {
        var result = _validator.Validate(new EnableFtpsCommand("ftp.example.test\n"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == ErrorCodes.FtpsHostnameInvalidFormat;
        });
    }

    /// <summary>A passive address carrying a newline is refused.</summary>
    /// <remarks>The same config-injection surface, on the second value an operator types.</remarks>
    [Fact]
    public void A_passive_address_carrying_a_newline_is_refused()
    {
        var result = _validator.Validate(new EnableFtpsCommand("ftp.example.test", "203.0.113.7\npasv_promiscuous=YES"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == ErrorCodes.FtpsPassiveAddressInvalid;
        });
    }

    /// <summary>A passive address that is a name rather than a literal is refused.</summary>
    /// <remarks>
    /// vsftpd would have to resolve a name at start-up, which makes whether the daemon comes back
    /// after a reboot depend on whether DNS answered first.
    /// </remarks>
    [Fact]
    public void A_passive_address_that_is_a_name_rather_than_a_literal_is_refused()
    {
        var result = _validator.Validate(new EnableFtpsCommand("ftp.example.test", "nat.example.test"));

        Assert.False(result.IsValid);
    }

    /// <summary>A literal ipv4 passive address is accepted.</summary>
    /// <remarks>
    /// The inverse control for the family rules below: a validator narrowed until it refuses every
    /// address would pass every test that only ever hands it one of the wrong family
    /// (rules/testing.md).
    /// </remarks>
    [Fact]
    public void A_literal_ipv4_passive_address_is_accepted()
    {
        var result = _validator.Validate(new EnableFtpsCommand("ftp.example.test", "203.0.113.7"));

        Assert.True(result.IsValid);
    }

    /// <summary>An IPv6 passive address is refused.</summary>
    /// <remarks>
    /// <para>
    /// The seam this test closes. <c>pasv_address</c> is an IPv4-only directive — the PASV reply of
    /// RFC 959 carries four decimal octets and has no IPv6 form — so the agent's
    /// <c>PassiveAddress</c> refuses the wrong family through its own error variant. While this
    /// validator admitted anything <c>IPAddress.TryParse</c> parsed, an operator who typed an IPv6
    /// literal was accepted at the form and refused one process later, which reaches them as a
    /// server failure instead of a 400 that names the field.
    /// </para>
    /// <para>
    /// The CODE is asserted and not merely the refusal, because the sentence behind it is where the
    /// constraint is named: it says IPv4, and says that the PASV reply has no IPv6 form, so the
    /// reader of <c>2001:db8::1</c> is not sent hunting a typo in a value that has none.
    /// </para>
    /// </remarks>
    [Fact]
    public void An_ipv6_passive_address_is_refused()
    {
        var result = _validator.Validate(new EnableFtpsCommand("ftp.example.test", "2001:db8::1"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == ErrorCodes.FtpsPassiveAddressInvalid;
        });
    }

    /// <summary>An IPv6 passive address produces exactly one message, not two.</summary>
    /// <remarks>
    /// One field, one rule, one sentence. A second rule added for the family would report its own
    /// message beside this one, and an operator reading two refusals for one value cannot tell which
    /// of them to act on.
    /// </remarks>
    [Fact]
    public void An_ipv6_passive_address_produces_exactly_one_message()
    {
        var result = _validator.Validate(new EnableFtpsCommand("ftp.example.test", "2001:db8::1"));

        Assert.Single(result.Errors);
    }

    /// <summary>An IPv4-mapped IPv6 passive address is refused, as the agent refuses it.</summary>
    /// <remarks>
    /// The spelling that would slip past a colon-hunting check and past a family check written the
    /// other way round: <c>::ffff:203.0.113.7</c> is an IPv6 literal that contains a dotted quad,
    /// and .NET reports its family as IPv6 exactly as Rust's IPv6 parser accepts it. Both sides
    /// therefore refuse it, and this pins that they refuse it for the same reason.
    /// </remarks>
    [Fact]
    public void An_ipv4_mapped_ipv6_passive_address_is_refused()
    {
        var result = _validator.Validate(new EnableFtpsCommand("ftp.example.test", "::ffff:203.0.113.7"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == ErrorCodes.FtpsPassiveAddressInvalid;
        });
    }

    /// <summary>A passive address with a leading-zero octet is refused.</summary>
    /// <remarks>
    /// <para>
    /// The second half of the same seam, and it was MEASURED rather than assumed: .NET's parser
    /// accepts <c>010.0.0.1</c> as IPv4 while the agent's <c>Ipv4Addr</c> grammar refuses it, so a
    /// family check alone would have left this divergence in place. The first run of this test, with
    /// the family check in and no spelling comparison, went red here.
    /// </para>
    /// <para>
    /// It matters beyond tidiness: a leading-zero octet is read as octal by some resolvers and as
    /// decimal by others, which is two different hosts behind one spelling. The validator refuses it
    /// by rendering the parsed address back and comparing, so the accepted set is the addresses the
    /// panel would itself have written.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_passive_address_with_a_leading_zero_octet_is_refused()
    {
        var result = _validator.Validate(new EnableFtpsCommand("ftp.example.test", "010.0.0.1"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == ErrorCodes.FtpsPassiveAddressInvalid;
        });
    }

    /// <summary>An empty passive address is accepted, because empty is how the contract spells absence.</summary>
    /// <remarks>
    /// This is the ordinary host — every host not behind NAT — and the screen's blank field arrives
    /// here as <c>""</c>. Refusing it, which an earlier version of this validator did, would have
    /// told an operator with nothing wrong that their blank was an invalid address and left FTPS
    /// unswitchable on. The agent contract already gives empty this exact meaning: the
    /// <c>pasv_address</c> key is not written at all.
    /// </remarks>
    [Fact]
    public void An_empty_passive_address_is_accepted_because_empty_is_how_the_contract_spells_absence()
    {
        var result = _validator.Validate(new EnableFtpsCommand("ftp.example.test", string.Empty));

        Assert.True(result.IsValid);
    }
}
