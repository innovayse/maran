using Maran.Modules.Accounts.Commands.ChangeAccountOwnerEmail;

namespace Maran.Modules.Accounts.Tests.Commands.ChangeAccountOwnerEmail;

/// <summary>Behavioral contract of <see cref="ChangeAccountOwnerEmailCommandValidator"/>.</summary>
public sealed class ChangeAccountOwnerEmailCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ChangeAccountOwnerEmailCommandValidator _validator = new();

    /// <summary>A bare address is accepted.</summary>
    [Fact]
    public void A_bare_address_is_accepted()
    {
        Assert.True(_validator.Validate(new ChangeAccountOwnerEmailCommand("owner@example.com")).IsValid);
    }

    /// <summary>An empty, display-name, control-character or over-long address is refused.</summary>
    /// <param name="email">The address submitted.</param>
    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("Ops Team <ops@example.com>")]
    [InlineData("owner@example.com\r\nBcc: x@example.com")]
    public void A_malformed_address_is_refused(string email)
    {
        Assert.False(_validator.Validate(new ChangeAccountOwnerEmailCommand(email)).IsValid);
    }

    /// <summary>An address wider than the narrower of the two columns it is written to is refused.</summary>
    [Fact]
    public void An_address_wider_than_the_narrower_of_the_two_columns_it_is_written_to_is_refused()
    {
        var email = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 61) + ".com";

        Assert.True(email.Length > 254);
        Assert.False(_validator.Validate(new ChangeAccountOwnerEmailCommand(email)).IsValid);
    }
}
