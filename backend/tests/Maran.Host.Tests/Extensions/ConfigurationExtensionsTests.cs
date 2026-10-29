using Maran.Host.Configuration;
using Maran.Host.Extensions;
using Maran.Host.Tests.Fixtures;
using Maran.Modules.Identity.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Maran.Host.Tests.Extensions;

/// <summary>
/// Behavioral contract of the key-value refusals wired in
/// <see cref="Host.Extensions.ConfigurationExtensions.AddPanelConfiguration"/>, read WITHOUT a host start: a bare
/// service collection, an in-memory configuration and a fixed environment name. This is the path a
/// command that builds the container but never runs the host takes, where the validators fire when
/// the options are first read.
/// </summary>
public sealed class ConfigurationExtensionsTests
{
    /// <summary>The published development key, base64, as the sample files carry it.</summary>
    private const string PublishedKey = PanelTestSettings.EncryptionKey;

    /// <summary>
    /// Printable text with more than sixteen distinct bytes: held as structured by the printable
    /// half of the low-entropy check alone.
    /// </summary>
    private static readonly string TextKey = Convert.ToBase64String("abcdefghijklmnopqrstuvwxyz012345"u8.ToArray());

    /// <summary>
    /// Thirty-two bytes of 0x80..0x8F twice: not printable, sixteen distinct values. Held as
    /// structured by the distinct-byte half of the low-entropy check alone.
    /// </summary>
    private static readonly string PatternedKey = Convert.ToBase64String(
        Enumerable.Range(0, 32).Select(index =>
        {
            return (byte)(0x80 + (index % 16));
        }).ToArray());

    /// <summary>A well-formed, random-looking encryption key, base64.</summary>
    private static readonly string GoodEncryptionKey = DerivedTestKeys.Base64("encryption", 32);

    /// <summary>A well-formed, random-looking signing key, base64.</summary>
    private static readonly string GoodSigningKey = DerivedTestKeys.Base64("signing", 32);

    /// <summary>A published encryption key is refused in Production when the options are read, and named.</summary>
    [Fact]
    public void Reading_security_options_in_production_refuses_the_published_key_without_a_host_start()
    {
        var failure = SecurityFailure("Production", PublishedKey);

        var message = Assert.Single(failure.Failures);
        Assert.Contains("Security:EncryptionKey", message, StringComparison.Ordinal);
        Assert.Contains("published", message, StringComparison.Ordinal);
        Assert.Contains("openssl rand -base64 32", message, StringComparison.Ordinal);
        Assert.Contains("re-encryption", message, StringComparison.Ordinal);
    }

    /// <summary>A refusal never carries a byte of the key it refuses, in either spelling.</summary>
    [Fact]
    public void A_refusal_never_echoes_the_key()
    {
        var planted = "prefix " + PublishedKey + " suffix";
        Assert.Contains(PublishedKey, planted, StringComparison.Ordinal);

        var message = Assert.Single(SecurityFailure("Production", PublishedKey).Failures);

        Assert.DoesNotContain(PublishedKey, message, StringComparison.Ordinal);
        Assert.DoesNotContain("0123456789abcdef", message, StringComparison.Ordinal);
    }

    /// <summary>An encryption key that is text is refused in Production as too structured, not as published.</summary>
    [Fact]
    public void Reading_security_options_in_production_refuses_an_encryption_key_that_is_text()
    {
        var message = Assert.Single(SecurityFailure("Production", TextKey).Failures);

        Assert.Contains("Security:EncryptionKey", message, StringComparison.Ordinal);
        Assert.Contains("too structured", message, StringComparison.Ordinal);
        Assert.DoesNotContain("published", message, StringComparison.Ordinal);
    }

    /// <summary>An encryption key made of a short repeated pattern is refused in Production.</summary>
    [Fact]
    public void Reading_security_options_in_production_refuses_a_patterned_encryption_key()
    {
        var message = Assert.Single(SecurityFailure("Production", PatternedKey).Failures);

        Assert.Contains("too structured", message, StringComparison.Ordinal);
    }

    /// <summary>A malformed key reports only its format problem, with no policy noise beside it.</summary>
    [Fact]
    public void Reading_security_options_in_production_reports_only_the_format_of_a_short_key()
    {
        var shortKey = DerivedTestKeys.Base64("short-encryption", 12);

        var message = Assert.Single(SecurityFailure("Production", shortKey).Failures);

        Assert.Contains("256-bit key", message, StringComparison.Ordinal);
        Assert.DoesNotContain("published", message, StringComparison.Ordinal);
        Assert.DoesNotContain("structured", message, StringComparison.Ordinal);
    }

    /// <summary>A published signing key is refused in Production when the options are read, and named.</summary>
    [Fact]
    public void Reading_jwt_options_in_production_refuses_the_published_signing_key()
    {
        var failure = JwtFailure("Production", GoodEncryptionKey, PublishedKey);

        var message = Assert.Single(failure.Failures);
        Assert.Contains("Jwt:SigningKey", message, StringComparison.Ordinal);
        Assert.Contains("published", message, StringComparison.Ordinal);
        Assert.Contains("signed out", message, StringComparison.Ordinal);
        Assert.Contains("no data is lost", message, StringComparison.Ordinal);
        Assert.DoesNotContain(PublishedKey, message, StringComparison.Ordinal);
    }

    /// <summary>A malformed signing key reports only its format problem, with no policy noise beside it.</summary>
    [Fact]
    public void Reading_jwt_options_in_production_reports_only_the_format_of_a_short_signing_key()
    {
        var shortKey = DerivedTestKeys.Base64("short-signing", 12);

        var message = Assert.Single(JwtFailure("Production", GoodEncryptionKey, shortKey).Failures);

        Assert.Contains("at least 32 bytes", message, StringComparison.Ordinal);
        Assert.DoesNotContain("published", message, StringComparison.Ordinal);
        Assert.DoesNotContain("structured", message, StringComparison.Ordinal);
    }

    /// <summary>A signing key made of a short repeated pattern is refused in Production.</summary>
    [Fact]
    public void Reading_jwt_options_in_production_refuses_a_patterned_signing_key()
    {
        var message = Assert.Single(JwtFailure("Production", GoodEncryptionKey, PatternedKey).Failures);

        Assert.Contains("Jwt:SigningKey", message, StringComparison.Ordinal);
        Assert.Contains("too structured", message, StringComparison.Ordinal);
    }

    /// <summary>A signing key that is text is refused in Production as too structured.</summary>
    [Fact]
    public void Reading_jwt_options_in_production_refuses_a_signing_key_that_is_text()
    {
        var message = Assert.Single(JwtFailure("Production", GoodEncryptionKey, TextKey).Failures);

        Assert.Contains("too structured", message, StringComparison.Ordinal);
        Assert.DoesNotContain("published", message, StringComparison.Ordinal);
    }

    /// <summary>The signing key is compared against the configured encryption key, so equal keys are refused.</summary>
    [Fact]
    public void Reading_jwt_options_compares_against_the_configured_encryption_key()
    {
        var shared = DerivedTestKeys.Base64("shared", 32);

        var message = Assert.Single(JwtFailure("Production", shared, shared).Failures);

        Assert.Contains("same key", message, StringComparison.Ordinal);
        Assert.Contains("Jwt:SigningKey", message, StringComparison.Ordinal);
        Assert.Contains("Security:EncryptionKey", message, StringComparison.Ordinal);
        Assert.Contains("Replace the signing key", message, StringComparison.Ordinal);
    }

    /// <summary>A signing key that contains the encryption key is the same key and is refused.</summary>
    [Fact]
    public void Reading_jwt_options_in_production_refuses_a_signing_key_that_contains_the_encryption_key()
    {
        var encryption = GoodEncryptionKey;
        var signing = Convert.ToBase64String(
            Convert.FromBase64String(DerivedTestKeys.Base64("head", 32))
                .Concat(Convert.FromBase64String(encryption))
                .Concat(Convert.FromBase64String(DerivedTestKeys.Base64("tail", 32)))
                .ToArray());

        var message = Assert.Single(JwtFailure("Production", encryption, signing).Failures);

        Assert.Contains("same key", message, StringComparison.Ordinal);
    }

    /// <summary>Two independent random-looking keys are accepted in Production (the inverse control).</summary>
    [Fact]
    public void Reading_options_in_production_accepts_two_independent_random_keys()
    {
        using var provider = Build("Production", GoodEncryptionKey, GoodSigningKey);

        var security = provider.GetRequiredService<IOptions<SecurityOptions>>().Value;
        var jwt = provider.GetRequiredService<IOptions<JwtOptions>>().Value;

        Assert.Equal(GoodEncryptionKey, security.EncryptionKey);
        Assert.Equal(GoodSigningKey, jwt.SigningKey);
    }

    /// <summary>The published sample keys are admitted in the environments the allow-list names, in any case.</summary>
    /// <param name="environmentName">The environment name.</param>
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("testing")]
    [InlineData("DEVELOPMENT")]
    public void Reading_options_admits_the_published_sample_keys_in_development_and_testing(string environmentName)
    {
        using var provider = Build(environmentName, PublishedKey, PublishedKey);

        Assert.Equal(PublishedKey, provider.GetRequiredService<IOptions<SecurityOptions>>().Value.EncryptionKey);
        Assert.Equal(PublishedKey, provider.GetRequiredService<IOptions<JwtOptions>>().Value.SigningKey);
    }

    /// <summary>Every other environment name refuses the sample keys: the list is an allow-list.</summary>
    /// <param name="environmentName">The environment name.</param>
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Prod")]
    [InlineData("Test")]
    [InlineData("Developmentx")]
    [InlineData("")]
    public void Reading_options_refuses_the_published_sample_keys_outside_the_allow_list(string environmentName)
    {
        Assert.Contains(
            "published",
            Assert.Single(SecurityFailure(environmentName, PublishedKey).Failures),
            StringComparison.Ordinal);
        Assert.Contains(
            "published",
            Assert.Single(JwtFailure(environmentName, GoodEncryptionKey, PublishedKey).Failures),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads <see cref="SecurityOptions"/> in <paramref name="environmentName"/> with
    /// <paramref name="encryptionKey"/> configured and returns the refusal.
    /// </summary>
    /// <param name="environmentName">The environment name.</param>
    /// <param name="encryptionKey">The encryption key, base64.</param>
    /// <returns>The validation failure the read raised.</returns>
    private static OptionsValidationException SecurityFailure(string environmentName, string encryptionKey)
    {
        using var provider = Build(environmentName, encryptionKey, GoodSigningKey);
        var options = provider.GetRequiredService<IOptions<SecurityOptions>>();

        return Assert.Throws<OptionsValidationException>(() =>
        {
            return options.Value;
        });
    }

    /// <summary>
    /// Reads <see cref="JwtOptions"/> in <paramref name="environmentName"/> with both keys configured
    /// and returns the refusal.
    /// </summary>
    /// <param name="environmentName">The environment name.</param>
    /// <param name="encryptionKey">The encryption key, base64.</param>
    /// <param name="signingKey">The signing key, base64.</param>
    /// <returns>The validation failure the read raised.</returns>
    private static OptionsValidationException JwtFailure(
        string environmentName,
        string encryptionKey,
        string signingKey)
    {
        using var provider = Build(environmentName, encryptionKey, signingKey);
        var options = provider.GetRequiredService<IOptions<JwtOptions>>();

        return Assert.Throws<OptionsValidationException>(() =>
        {
            return options.Value;
        });
    }

    /// <summary>
    /// Builds a container holding the panel's options over an in-memory configuration and a fixed
    /// environment, without a host.
    /// </summary>
    /// <param name="environmentName">The environment name.</param>
    /// <param name="encryptionKey">The encryption key, base64.</param>
    /// <param name="signingKey">The signing key, base64.</param>
    /// <returns>The service provider; the caller disposes it.</returns>
    private static ServiceProvider Build(string environmentName, string encryptionKey, string signingKey)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [PanelTestSettings.EncryptionKeyPath] = encryptionKey,
                    [PanelTestSettings.JwtSigningKeyPath] = signingKey,
                })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSingleton<IHostEnvironment>(new FixedHostEnvironment(environmentName));
        services.AddPanelConfiguration(configuration);

        return services.BuildServiceProvider();
    }
}
