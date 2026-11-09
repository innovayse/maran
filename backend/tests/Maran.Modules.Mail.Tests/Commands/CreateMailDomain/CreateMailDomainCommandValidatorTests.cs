using Maran.Modules.Mail.Commands.CreateMailDomain;

namespace Maran.Modules.Mail.Tests.Commands.CreateMailDomain;

/// <summary>What a mail domain name may look like before the handler is ever reached.</summary>
public sealed class CreateMailDomainCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly CreateMailDomainCommandValidator _validator = new();

    /// <summary>A well-formed name is accepted whatever its case.</summary>
    /// <param name="name">The name.</param>
    [Theory]
    [InlineData("example.com")]
    [InlineData("Example.COM")]
    [InlineData("mail.sub.example.co.uk")]
    [InlineData("xn--bcher-kva.example")]
    public void A_well_formed_name_is_accepted_in_any_case(string name)
    {
        var result = _validator.Validate(new CreateMailDomainCommand(Guid.NewGuid(), name));

        Assert.True(result.IsValid);
    }

    /// <summary>A name that is not a host name of two or more labels is refused with the invalid code.</summary>
    /// <param name="name">The name.</param>
    [Theory]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("example")]
    [InlineData("-example.com")]
    [InlineData("example-.com")]
    [InlineData("exa mple.com")]
    [InlineData("example.com\n")]
    [InlineData("user@example.com")]
    [InlineData("127.0.0.1")]
    public void A_name_that_is_not_a_host_name_is_refused(string name)
    {
        var result = _validator.Validate(new CreateMailDomainCommand(Guid.NewGuid(), name));

        Assert.False(result.IsValid);
        Assert.Equal("MailDomainInvalid", result.Errors[0].ErrorMessage);
    }

    /// <summary>A name under a reserved suffix is refused as invalid.</summary>
    [Fact]
    public void A_reserved_name_is_refused()
    {
        var result = _validator.Validate(new CreateMailDomainCommand(Guid.NewGuid(), "printer.local"));

        Assert.False(result.IsValid);
        Assert.Equal("MailDomainInvalid", result.Errors[0].ErrorMessage);
    }

    /// <summary>A name beyond the module's ceiling is refused with the too-long code.</summary>
    [Fact]
    public void A_name_beyond_the_ceiling_is_refused_as_too_long()
    {
        var name = string.Concat(new string('a', 63), ".", new string('b', 63), ".", new string('c', 63), ".com");

        var result = _validator.Validate(new CreateMailDomainCommand(Guid.NewGuid(), name));

        Assert.False(result.IsValid);
        Assert.Equal("MailDomainTooLong", result.Errors[0].ErrorMessage);
    }

    /// <summary>An empty account id is refused.</summary>
    [Fact]
    public void An_empty_account_id_is_refused()
    {
        var result = _validator.Validate(new CreateMailDomainCommand(Guid.Empty, "example.com"));

        Assert.False(result.IsValid);
    }
}
