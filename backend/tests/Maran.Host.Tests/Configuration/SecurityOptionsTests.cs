using Microsoft.Extensions.Options;

namespace Maran.Host.Tests.Configuration;

/// <summary>
/// Behavioral contract of the startup validation wired in
/// <see cref="Host.Extensions.ConfigurationExtensions.AddPanelConfiguration"/> for
/// <see cref="Host.Configuration.SecurityOptions"/>: a misconfigured encryption key must fail the
/// boot, never the first request (rules/security.md "Secrets").
/// </summary>
public sealed class SecurityOptionsTests
{
    [Fact]
    public void Missing_encryption_key_fails_startup()
    {
        using var factory = new PanelTestFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Security:EncryptionKey", string.Empty);
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
        {
            return _ = factory.Services;
        });

        Assert.Contains("EncryptionKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Too_short_encryption_key_fails_startup()
    {
        var tooShort = Convert.ToBase64String("not-32-bytes"u8.ToArray());

        using var factory = new PanelTestFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Security:EncryptionKey", tooShort);
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
        {
            return _ = factory.Services;
        });

        Assert.Contains("256-bit key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Valid_encryption_key_boots_successfully()
    {
        using var factory = new PanelTestFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(PanelTestSettings.EncryptionKeyPath, PanelTestSettings.EncryptionKey);
        });

        // Forces the host to actually build and start; a validation failure here throws.
        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }
}
