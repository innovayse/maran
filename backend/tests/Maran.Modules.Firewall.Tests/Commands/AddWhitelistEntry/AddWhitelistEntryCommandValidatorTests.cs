using Maran.Modules.Firewall.Commands.AddWhitelistEntry;

namespace Maran.Modules.Firewall.Tests.Commands.AddWhitelistEntry;

/// <summary>What a whitelist row has to look like before it is allowed to exist.</summary>
public sealed class AddWhitelistEntryCommandValidatorTests
{
    /// <summary>A range with host bits beyond its prefix is refused rather than masked.</summary>
    [Fact]
    public void A_range_with_host_bits_beyond_its_prefix_is_refused_rather_than_masked()
    {
        // An exemption is exactly the thing that must not be wider than the person who wrote it
        // believes: 203.0.113.7/24 exempts either one machine or two hundred and fifty-six.
        var result = Validate("203.0.113.7/24");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == "WhitelistCidrInvalid";
        });
    }

    /// <summary>A value that is not a range at all is refused.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("203.0.113.7")]
    [InlineData("the office")]
    [InlineData("fe80::1%eth0/128")]
    public void A_value_that_is_not_a_range_at_all_is_refused(string cidr)
    {
        // A row that cannot be parsed matches no packet that ever arrives, so an administrator
        // reading it back would believe they were exempt while they were not — worse than having no
        // row at all, because the false one stops them adding a real one.
        Assert.False(Validate(cidr).IsValid);
    }

    /// <summary>A single host range is accepted.</summary>
    [Fact]
    public void A_single_host_range_is_accepted()
    {
        // The shape the installer seeds: the address the operator installed the panel from.
        Assert.True(Validate("203.0.113.7/32").IsValid);
    }

    /// <summary>A network range and an ipv6 range are accepted.</summary>
    [Theory]
    [InlineData("203.0.113.0/24")]
    [InlineData("2001:db8::/32")]
    [InlineData("2001:db8::7/128")]
    public void A_network_range_and_an_ipv6_range_are_accepted(string cidr)
    {
        Assert.True(Validate(cidr).IsValid);
    }

    /// <summary>An IPv4 mapped range is refused by the command that writes the row.</summary>
    [Theory]
    [InlineData("::ffff:198.51.100.10/128")]
    [InlineData("::ffff:0:0/96")]
    public void An_IPv4_mapped_range_is_refused_by_the_command_that_writes_the_row(string cidr)
    {
        // Tested HERE and not only on CidrRange, because both halves of this defect were
        // individually correct: the range parsed, and the matcher compared families properly. Only
        // the path an administrator actually walks — POST a row, have it stored, be banned anyway —
        // shows the two agreeing on nothing. The row used to come back 201 and exempt no one.
        var result = Validate(cidr);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == "WhitelistCidrInvalid";
        });
    }

    /// <summary>An omitted range is refused rather than throwing.</summary>
    [Fact]
    public void An_omitted_range_is_refused_rather_than_throwing()
    {
        // FluentValidation 12.1.1 runs the .Must(...) even after .NotEmpty() has already failed, so
        // a request that simply left the field out reached CidrRange with null and the endpoint
        // answered 500 instead of 400. Assert.False is the point; not throwing is the fix.
        Assert.False(Validate(null!).IsValid);
    }

    /// <summary>Runs the validator over one candidate row.</summary>
    /// <param name="cidr">The range the row would exempt.</param>
    private static FluentValidation.Results.ValidationResult Validate(string cidr)
    {
        return new AddWhitelistEntryCommandValidator().Validate(
            new AddWhitelistEntryCommand(cidr, "office", "198.51.100.1", "curl"));
    }
}
