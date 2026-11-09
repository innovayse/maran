using Maran.Modules.Accounts.Commands.UpdateReseller;

namespace Maran.Modules.Accounts.Tests.Commands.UpdateReseller;

/// <summary>
/// Behavioral contract of <see cref="UpdateResellerCommandValidator"/>: the name, address and language
/// are held to the same rules as at creation.
/// </summary>
public sealed class UpdateResellerCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly UpdateResellerCommandValidator _validator = new();

    /// <summary>Builds a command, valid unless a value is overridden.</summary>
    /// <param name="name">The name.</param>
    /// <param name="email">The contact address.</param>
    /// <param name="language">The language.</param>
    private static UpdateResellerCommand Command(
        string name = "acme",
        string email = "owner@acme.example",
        string language = "ru")
    {
        return new UpdateResellerCommand(name, email, language);
    }

    /// <summary>A valid change is accepted: the inverse control for the refusals below.</summary>
    [Fact]
    public void A_valid_change_is_accepted()
    {
        Assert.True(_validator.Validate(Command()).IsValid);
    }

    /// <summary>A name outside the account name alphabet or length is refused with the name code.</summary>
    /// <param name="name">The rejected name.</param>
    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("Acme")]
    [InlineData("1acme")]
    [InlineData("root")]
    [InlineData("acme-shop")]
    public void A_name_outside_the_alphabet_or_length_is_refused_with_the_name_code(string name)
    {
        var result = _validator.Validate(Command(name: name));

        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == "ResellerNameInvalid";
        });
    }

    /// <summary>An address that is empty, malformed or carries a display name is refused.</summary>
    /// <param name="email">The rejected address.</param>
    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("Boss <boss@example.com>")]
    public void An_address_that_is_empty_or_malformed_is_refused(string email)
    {
        Assert.False(_validator.Validate(Command(email: email)).IsValid);
    }

    /// <summary>A language the panel does not ship is refused with the language code.</summary>
    [Fact]
    public void A_language_the_panel_does_not_ship_is_refused_with_the_language_code()
    {
        var result = _validator.Validate(Command(language: "xx"));

        Assert.Equal("AccountOwnerLanguageUnsupported", Assert.Single(result.Errors).ErrorMessage);
    }
}
