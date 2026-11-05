using Maran.Modules.Sftp.Commands.RemoveSftpUserKey;

namespace Maran.Modules.Sftp.Tests.Commands.RemoveSftpUserKey;

/// <summary>What the remove validator refuses: only an empty route identifier.</summary>
public sealed class RemoveSftpUserKeyCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly RemoveSftpUserKeyCommandValidator Validator = new();

    /// <summary>Two non-empty identifiers are accepted — the inverse control for the refusals below.</summary>
    [Fact]
    public void Two_non_empty_identifiers_are_accepted()
    {
        Assert.True(Validator.Validate(
            new RemoveSftpUserKeyCommand(Guid.NewGuid(), Guid.NewGuid(), "203.0.113.7", "tests")).IsValid);
    }

    /// <summary>An empty login identifier is refused.</summary>
    [Fact]
    public void An_empty_login_identifier_is_refused()
    {
        var result = Validator.Validate(
            new RemoveSftpUserKeyCommand(Guid.Empty, Guid.NewGuid(), "203.0.113.7", "tests"));

        Assert.Equal(nameof(RemoveSftpUserKeyCommand.SftpUserId), Assert.Single(result.Errors).PropertyName);
    }

    /// <summary>An empty key identifier is refused.</summary>
    [Fact]
    public void An_empty_key_identifier_is_refused()
    {
        var result = Validator.Validate(
            new RemoveSftpUserKeyCommand(Guid.NewGuid(), Guid.Empty, "203.0.113.7", "tests"));

        Assert.Equal(nameof(RemoveSftpUserKeyCommand.KeyId), Assert.Single(result.Errors).PropertyName);
    }
}
