using Maran.Modules.Licensing.Services;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Proves the production key source answers "no key" for anything but a well-formed key file, and
/// that this repository's build embeds no key until a real one exists (issue #470).
/// </summary>
public sealed class EmbeddedLicenceVerificationKeySourceTests
{
    /// <summary>The text of a well-formed key file.</summary>
    private const string WellFormed = "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F";

    /// <summary>A well-formed key file, surrounding whitespace included, yields a configured key.</summary>
    [Fact]
    public void A_well_formed_key_file_yields_a_configured_key()
    {
        var key = EmbeddedLicenceVerificationKeySource.Parse("  " + WellFormed + "\n");

        Assert.True(key.IsConfigured);
        Assert.Equal(Convert.FromHexString(WellFormed), key.PublicKey);
    }

    /// <summary>Text that is not 64 hexadecimal characters yields no key, never a throw.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not a key")]
    [InlineData("000102")]
    [InlineData("ZZ0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F")]
    public void Anything_else_yields_no_key(string text)
    {
        Assert.False(EmbeddedLicenceVerificationKeySource.Parse(text).IsConfigured);
    }

    /// <summary>An assembly without the embedded key file yields no key.</summary>
    [Fact]
    public void An_assembly_without_the_key_resource_yields_no_key()
    {
        Assert.False(EmbeddedLicenceVerificationKeySource.Read(typeof(EmbeddedLicenceVerificationKeySourceTests).Assembly)
            .IsConfigured);
    }
}
