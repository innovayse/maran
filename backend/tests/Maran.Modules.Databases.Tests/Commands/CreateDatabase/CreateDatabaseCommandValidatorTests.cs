using Maran.Modules.Databases.Commands.CreateDatabase;

namespace Maran.Modules.Databases.Tests.Commands.CreateDatabase;

/// <summary>What the create validator lets through to a root MySQL session, and what it refuses.</summary>
public sealed class CreateDatabaseCommandValidatorTests
{
    private static readonly CreateDatabaseCommandValidator Validator = new();

    /// <summary>A plain lowercase name is accepted.</summary>
    [Theory]
    [InlineData("shop")]
    [InlineData("shop2")]
    [InlineData("a")]
    [InlineData("0")]
    public void A_plain_lowercase_name_is_accepted(string name)
    {
        Assert.True(Validate(name, "shopuser").IsValid);
    }

    /// <summary>A name carrying anything outside lowercase letters and digits is refused.</summary>
    [Theory]
    [InlineData("Shop")]
    [InlineData("shop-1")]
    [InlineData("shop.1")]
    [InlineData("shop 1")]
    [InlineData("shop`1")]
    [InlineData("shop'1")]
    [InlineData("shop\"1")]
    [InlineData("shop\\1")]
    [InlineData("shop;drop")]
    [InlineData("")]
    public void A_name_carrying_anything_outside_lowercase_letters_and_digits_is_refused(string name)
    {
        // Both names are interpolated into DDL in a root MySQL session, which takes no placeholders,
        // so the alphabet is the whole of the injection defence: values are validated, not escaped.
        Assert.False(Validate(name, "shopuser").IsValid);
    }

    /// <summary>A name carrying a trailing newline is refused despite ending in a legal name.</summary>
    [Fact]
    public void A_name_carrying_a_trailing_newline_is_refused_despite_ending_in_a_legal_name()
    {
        // In .NET a `$`-anchored pattern also matches immediately before a trailing newline, so this
        // is the case a `$` instead of `\z` would let through — and a newline is what turns one
        // statement or one config line into two.
        Assert.False(Validate("shop\n", "shopuser").IsValid);
    }

    /// <summary>A name carrying the account separator is refused.</summary>
    [Fact]
    public void A_name_carrying_the_account_separator_is_refused()
    {
        // Account names may contain an underscore, so a suffix that could hold one would let account
        // `alice` ask for `bob_secrets` and be handed `alice_bob_secrets` — a name that reads as
        // account `bob`'s in every listing, log line and backup file an operator will ever see.
        Assert.False(Validate("bob_secrets", "shopuser").IsValid);
    }

    /// <summary>A user name carrying anything outside lowercase letters and digits is refused.</summary>
    [Theory]
    [InlineData("Shop")]
    [InlineData("shop_user")]
    [InlineData("shop'1")]
    [InlineData("")]
    public void A_user_name_carrying_anything_outside_lowercase_letters_and_digits_is_refused(string dbUserName)
    {
        Assert.False(Validate("shop", dbUserName).IsValid);
    }

    /// <summary>An empty account identifier is refused.</summary>
    [Fact]
    public void An_empty_account_identifier_is_refused()
    {
        var result = Validator.Validate(
            new CreateDatabaseCommand(Guid.Empty, "shop", "shopuser", "203.0.113.7", "tests"));

        Assert.False(result.IsValid);
    }

    /// <summary>Runs the validator over one pair of names.</summary>
    /// <param name="name">The database name suffix.</param>
    /// <param name="dbUserName">The user name suffix.</param>
    private static FluentValidation.Results.ValidationResult Validate(string name, string dbUserName)
    {
        return Validator.Validate(
            new CreateDatabaseCommand(Guid.NewGuid(), name, dbUserName, "203.0.113.7", "tests"));
    }
}
