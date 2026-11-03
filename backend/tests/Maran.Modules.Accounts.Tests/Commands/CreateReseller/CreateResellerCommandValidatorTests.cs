using Maran.Modules.Accounts.Commands.CreateReseller;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Commands.CreateReseller;

/// <summary>
/// Behavioral contract of <see cref="CreateResellerCommandValidator"/>: the name must be a valid
/// login name, the address a valid recipient, the language a supported one and the envelope valid.
/// </summary>
public sealed class CreateResellerCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly CreateResellerCommandValidator _validator = new();

    /// <summary>Builds a command that is valid except for what a test overrides.</summary>
    /// <param name="name">The reseller's name.</param>
    /// <param name="email">The contact address.</param>
    /// <param name="language">The contact language.</param>
    private static CreateResellerCommand Valid(
        string name = "acme_hosting",
        string email = "owner@acme.example",
        string language = "en")
    {
        return new CreateResellerCommand(name, email, ResellerEnvelopeDtoTestData.Valid(), language);
    }

    /// <summary>A valid command is accepted: the inverse control for every refusal below.</summary>
    [Fact]
    public void A_valid_command_is_accepted()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
    }

    /// <summary>The shortest and the longest acceptable names are accepted.</summary>
    [Fact]
    public void The_shortest_and_the_longest_acceptable_names_are_accepted()
    {
        Assert.True(_validator.Validate(Valid(name: "abc")).IsValid);
        Assert.True(_validator.Validate(Valid(name: new string('a', 30))).IsValid);
    }

    /// <summary>A name that is too short, too long, badly spelled or reserved is refused with the name code.</summary>
    [Theory]
    [InlineData("ab")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("Acme")]
    [InlineData("1acme")]
    [InlineData("ac-me")]
    [InlineData("ac me")]
    [InlineData("root")]
    [InlineData("")]
    public void An_unacceptable_name_is_refused_with_the_name_code(string name)
    {
        var result = _validator.Validate(Valid(name: name));

        Assert.Equal("ResellerNameInvalid", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>An empty, malformed or over-long contact address is refused.</summary>
    [Fact]
    public void An_empty_malformed_or_over_long_contact_address_is_refused()
    {
        Assert.False(_validator.Validate(Valid(email: string.Empty)).IsValid);
        Assert.False(_validator.Validate(Valid(email: "not-an-address")).IsValid);
        Assert.False(_validator.Validate(Valid(email: "Name <a@b.example>")).IsValid);
        Assert.False(_validator.Validate(Valid(email: new string('a', 250) + "@example.com")).IsValid);
    }

    /// <summary>A language the panel does not support is refused with the owner-language code.</summary>
    [Fact]
    public void A_language_the_panel_does_not_support_is_refused()
    {
        var result = _validator.Validate(Valid(language: "xx"));

        Assert.Equal("AccountOwnerLanguageUnsupported", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>Every supported language is accepted.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public void Every_supported_language_is_accepted(string language)
    {
        Assert.True(_validator.Validate(Valid(language: language)).IsValid);
    }

    /// <summary>An envelope left out is refused with the envelope code.</summary>
    [Fact]
    public void An_envelope_left_out_is_refused_with_the_envelope_code()
    {
        var command = new CreateResellerCommand("acme_hosting", "owner@acme.example", null!);

        var result = _validator.Validate(command);

        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == "ResellerEnvelopeInvalid";
        });
    }

    /// <summary>An envelope with a negative figure is refused with the envelope code.</summary>
    [Fact]
    public void An_envelope_with_a_negative_figure_is_refused_with_the_envelope_code()
    {
        var command = new CreateResellerCommand(
            "acme_hosting",
            "owner@acme.example",
            ResellerEnvelopeDtoTestData.Valid() with { MaxAccounts = -1 });

        var result = _validator.Validate(command);

        Assert.Equal("ResellerEnvelopeInvalid", Assert.Single(result.Errors).ErrorMessage);
    }
}
