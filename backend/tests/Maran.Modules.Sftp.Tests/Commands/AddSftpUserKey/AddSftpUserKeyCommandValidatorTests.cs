using FluentValidation.Results;
using Maran.Modules.Sftp.Commands.AddSftpUserKey;
using Maran.Modules.Sftp.Domain.Policies;
using Maran.Modules.Sftp.Tests.TestSupport;

namespace Maran.Modules.Sftp.Tests.Commands.AddSftpUserKey;

/// <summary>What the add validator lets through to the handler and the agent, and what it refuses.</summary>
public sealed class AddSftpUserKeyCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly AddSftpUserKeyCommandValidator Validator = new();

    /// <summary>A label and a valid key are accepted — the inverse control for every refusal below.</summary>
    [Fact]
    public void A_label_and_a_valid_key_are_accepted()
    {
        Assert.True(Validate("work laptop", SftpTestKeys.Ed25519).IsValid);
        Assert.True(Validate("  padded  ", SftpTestKeys.P384 + " user@host").IsValid);
        Assert.True(Validate(new string('l', SftpKeyLimits.MaxLabelLength), SftpTestKeys.Rsa3072).IsValid);
    }

    /// <summary>A label that is empty, blank, too long or carries a control character is refused with its code.</summary>
    /// <param name="label">The label.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two\nlines")]
    [InlineData("tab\there")]
    [InlineData("nul\0here")]
    public void A_label_that_is_empty_blank_or_carries_a_control_character_is_refused(string label)
    {
        var result = Validate(label, SftpTestKeys.Ed25519);

        Assert.Equal("SftpKeyLabelInvalid", Assert.Single(result.Errors).ErrorMessage);
        Assert.Equal(nameof(AddSftpUserKeyCommand.Label), Assert.Single(result.Errors).PropertyName);
    }

    /// <summary>A label over the limit is refused.</summary>
    [Fact]
    public void A_label_over_the_limit_is_refused()
    {
        var result = Validate(new string('l', SftpKeyLimits.MaxLabelLength + 1), SftpTestKeys.Ed25519);

        Assert.Equal("SftpKeyLabelInvalid", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A key the grammar refuses is refused with the key code and the message never echoes the key.</summary>
    /// <param name="publicKey">The pasted text.</param>
    [Theory]
    [InlineData("")]
    [InlineData("not a key")]
    [InlineData("command=\"/bin/true\" " + SftpTestKeys.Ed25519)]
    [InlineData(SftpTestKeys.Rsa1024)]
    [InlineData(SftpTestKeys.Ed25519 + "\n" + SftpTestKeys.P256)]
    public void A_key_the_grammar_refuses_is_refused_without_echoing_it(string publicKey)
    {
        var result = Validate("laptop", publicKey);

        var error = Assert.Single(result.Errors);
        Assert.Equal("SftpKeyInvalid", error.ErrorMessage);
        Assert.Equal(nameof(AddSftpUserKeyCommand.PublicKey), error.PropertyName);
        Assert.DoesNotContain("ssh-", error.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>A bad label and a bad key are both reported.</summary>
    [Fact]
    public void A_bad_label_and_a_bad_key_are_both_reported()
    {
        var result = Validate("", "nonsense");

        Assert.Equal(2, result.Errors.Count);
    }

    /// <summary>Runs the validator over one label and key.</summary>
    /// <param name="label">The label.</param>
    /// <param name="publicKey">The pasted key text.</param>
    private static ValidationResult Validate(string label, string publicKey)
    {
        return Validator.Validate(new AddSftpUserKeyCommand(label, publicKey, Guid.NewGuid(), "203.0.113.7", "tests"));
    }
}
