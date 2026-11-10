using Maran.Modules.ServerMigration.Commands.ConfirmRemoteServerHostKey;

namespace Maran.Modules.ServerMigration.Tests.Commands.ConfirmRemoteServerHostKey;

/// <summary>The shape of a host key fingerprint. Whether it is the right one is the handler's question and has its own tests.</summary>
public sealed class ConfirmRemoteServerHostKeyCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly ConfirmRemoteServerHostKeyCommandValidator Validator = new();

    /// <summary>A fingerprint ssh prints is accepted.</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    [Theory]
    [InlineData("SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("SHA256:nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8")]
    [InlineData("SHA256:a+b/c+d/e+f/g+h/i+j/k+l/m+n/o+p/q+r/s+t/u+v")]
    public void A_fingerprint_ssh_prints_is_accepted(string fingerprint)
    {
        var result = Validator.Validate(new ConfirmRemoteServerHostKeyCommand(fingerprint));

        Assert.True(result.IsValid);
    }

    /// <summary>Text that is not the shape of a fingerprint is refused with its own code.</summary>
    /// <param name="fingerprint">The text.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sha256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA!")]
    [InlineData("MD5:aa:bb:cc:dd:ee:ff:00:11:22:33:44:55:66:77:88:99")]
    [InlineData(" SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Text_that_is_not_a_fingerprint_is_refused(string fingerprint)
    {
        var result = Validator.Validate(new ConfirmRemoteServerHostKeyCommand(fingerprint));

        Assert.Contains(result.Errors, error =>
        {
            return error.ErrorCode == "FingerprintInvalid";
        });
    }
}
