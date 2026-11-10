using Maran.Modules.ServerMigration.Domain.Policies;

namespace Maran.Modules.ServerMigration.Tests.Domain.Policies;

/// <summary>The shape of a host key fingerprint, with an accepting control for every refusal.</summary>
public sealed class HostKeyFingerprintPolicyTests
{
    /// <summary>A real fingerprint is well formed.</summary>
    [Fact]
    public void A_real_fingerprint_is_well_formed()
    {
        Assert.True(HostKeyFingerprintPolicy.IsWellFormed("SHA256:nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8"));
    }

    /// <summary>Text with the wrong prefix, length or alphabet is not, nor is nothing.</summary>
    /// <param name="fingerprint">The text.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SHA256:")]
    [InlineData("SHA1:nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8")]
    [InlineData("SHA256:nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY=")]
    [InlineData("SHA256:nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5S-8")]
    public void Wrong_prefix_length_or_alphabet_is_not_well_formed(string? fingerprint)
    {
        Assert.False(HostKeyFingerprintPolicy.IsWellFormed(fingerprint));
    }
}
