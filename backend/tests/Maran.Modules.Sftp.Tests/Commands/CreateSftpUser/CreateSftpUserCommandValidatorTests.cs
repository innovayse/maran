using Maran.Modules.Sftp.Commands.CreateSftpUser;

namespace Maran.Modules.Sftp.Tests.Commands.CreateSftpUser;

/// <summary>What the create validator lets through to useradd and sshd_config, and what it refuses.</summary>
public sealed class CreateSftpUserCommandValidatorTests
{
    private static readonly CreateSftpUserCommandValidator Validator = new();

    /// <summary>A plain lowercase name is accepted.</summary>
    [Theory]
    [InlineData("deploy")]
    [InlineData("deploy2")]
    [InlineData("a")]
    [InlineData("0")]
    public void A_plain_lowercase_name_is_accepted(string name)
    {
        Assert.True(Validate(name).IsValid);
    }

    /// <summary>A name carrying anything outside lowercase letters and digits is refused.</summary>
    [Theory]
    [InlineData("Deploy")]
    [InlineData("deploy-1")]
    [InlineData("deploy.1")]
    [InlineData("deploy 1")]
    [InlineData("deploy`1")]
    [InlineData("deploy'1")]
    [InlineData("deploy\"1")]
    [InlineData("deploy\\1")]
    [InlineData("deploy;id")]
    [InlineData("")]
    public void A_name_carrying_anything_outside_lowercase_letters_and_digits_is_refused(string name)
    {
        // The value becomes a useradd argument, a path segment under a root-owned tree, and a line
        // in an sshd_config drop-in — none of which escapes anything, so the alphabet is the whole
        // of the defence: values are validated, not escaped.
        Assert.False(Validate(name).IsValid);
    }

    /// <summary>A name carrying a trailing newline is refused despite ending in a legal name.</summary>
    [Fact]
    public void A_name_carrying_a_trailing_newline_is_refused_despite_ending_in_a_legal_name()
    {
        // In .NET a `$`-anchored pattern also matches immediately before a trailing newline, so this
        // is the case a `$` instead of `\z` would let through — and sshd_config is line-oriented, so
        // a newline here appends directives of the caller's choosing to the SSH daemon's config.
        Assert.False(Validate("deploy\n").IsValid);
    }

    /// <summary>A name carrying the account separator is refused.</summary>
    [Fact]
    public void A_name_carrying_the_account_separator_is_refused()
    {
        // Account names may contain an underscore, so a suffix that could hold one would let account
        // `alice` ask for `bob_deploy` and be handed `alice_bob_deploy` — a login that reads as
        // account `bob`'s in /etc/passwd and in every audit entry an operator will ever see.
        Assert.False(Validate("bob_deploy").IsValid);
    }

    /// <summary>An empty account identifier is refused.</summary>
    [Fact]
    public void An_empty_account_identifier_is_refused()
    {
        var result = Validator.Validate(
            new CreateSftpUserCommand(Guid.Empty, "deploy", "203.0.113.7", "tests"));

        Assert.False(result.IsValid);
    }

    /// <summary>Runs the validator over one name.</summary>
    /// <param name="name">The login name suffix.</param>
    private static FluentValidation.Results.ValidationResult Validate(string name)
    {
        return Validator.Validate(new CreateSftpUserCommand(Guid.NewGuid(), name, "203.0.113.7", "tests"));
    }
}
