using Maran.Modules.Databases.Commands.CreateDatabaseUser;

namespace Maran.Modules.Databases.Tests.Commands.CreateDatabaseUser;

/// <summary>The alphabet and length a user name suffix is held to before it reaches the handler.</summary>
public sealed class CreateDatabaseUserCommandValidatorTests
{
    private static readonly CreateDatabaseUserCommandValidator Validator = new();

    /// <summary>Lowercase letters and digits are accepted.</summary>
    [Theory]
    [InlineData("reader")]
    [InlineData("app2")]
    [InlineData("0")]
    public void Lowercase_letters_and_digits_are_accepted(string name)
    {
        Assert.True(Validator.Validate(Command(name)).IsValid);
    }

    /// <summary>Anything outside the alphabet is refused with the invalid format code.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("Reader")]
    [InlineData("my_user")]
    [InlineData("a-b")]
    [InlineData("a b")]
    [InlineData("a'b")]
    [InlineData("a`b")]
    [InlineData("a;b")]
    [InlineData("reader\n")]
    public void Anything_outside_the_alphabet_is_refused(string name)
    {
        Assert.False(Validator.Validate(Command(name)).IsValid);
    }

    /// <summary>A name beyond the coarse ceiling is refused and one at it is accepted.</summary>
    [Fact]
    public void A_name_beyond_the_coarse_ceiling_is_refused_and_one_at_it_is_accepted()
    {
        Assert.True(Validator.Validate(Command(new string('a', 30))).IsValid);
        Assert.False(Validator.Validate(Command(new string('a', 31))).IsValid);
    }

    /// <summary>An empty account identifier is refused.</summary>
    [Fact]
    public void An_empty_account_identifier_is_refused()
    {
        Assert.False(Validator.Validate(new CreateDatabaseUserCommand(Guid.Empty, "reader")).IsValid);
    }

    /// <summary>The refusal carries the resx key and not a sentence.</summary>
    [Fact]
    public void The_refusal_carries_the_resx_key_and_not_a_sentence()
    {
        var result = Validator.Validate(Command("Bad_Name"));

        Assert.Contains(result.Errors, error => { return error.ErrorMessage == "DatabaseUserNameInvalidFormat"; });
    }

    /// <summary>Builds a command for the owner.</summary>
    /// <param name="name">The name suffix.</param>
    private static CreateDatabaseUserCommand Command(string name)
    {
        return new CreateDatabaseUserCommand(Guid.NewGuid(), name);
    }
}
