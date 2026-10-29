using System.Net;
using System.Text.RegularExpressions;
using Maran.Host.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Maran.Host.Tests.Configuration;

/// <summary>
/// Behavioral contract of the key-value refusals wired in
/// <see cref="Host.Extensions.ConfigurationExtensions.AddPanelConfiguration"/>: outside the
/// Development and Testing environments the panel refuses to start with the development key this
/// repository publishes, with a key too structured to be random, or with a signing key that is, or
/// contains, the encryption key. The real <c>Program</c> is booted, and the refusal must come at
/// start, not at the first request (rules/security.md "Secrets").
/// </summary>
/// <remarks>
/// A <c>WebApplicationFactory</c> cannot leave the environment unset, which is how the installed
/// service runs; <c>Production</c> is the nearest in-process stand-in and is what an unset
/// environment resolves to.
/// </remarks>
public sealed class DevelopmentKeyRefusalTests
{
    /// <summary>The development key this repository publishes, as it appears in the sample files.</summary>
    private const string PublishedKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The plain text the published key decodes to; a refusal must not echo it either.</summary>
    private const string PublishedKeyText = "0123456789abcdef";

    /// <summary>Configuration path of the encryption key.</summary>
    private const string EncryptionPath = "Security:EncryptionKey";

    /// <summary>Configuration path of the signing key.</summary>
    private const string SigningPath = "Jwt:SigningKey";

    /// <summary>A printable-text key of 32 bytes holding more than sixteen distinct characters.</summary>
    private const string PassphraseText = "Tr0ub4dor&3-correct-horse-STAPLE";

    /// <summary>
    /// A random-looking 32-byte encryption key, derived rather than generated so a run repeats.
    /// </summary>
    private static readonly string GoodEncryptionKey = DerivedTestKeys.Base64("integration-encryption", 32);

    /// <summary>A random-looking 64-byte signing key, independent of <see cref="GoodEncryptionKey"/>.</summary>
    private static readonly string GoodSigningKey = DerivedTestKeys.Base64("integration-signing", 64);

    /// <summary>Boots with the keys of the sample file an operator copies, outside the allow-list.</summary>
    [Fact]
    public void A_production_host_with_the_env_example_keys_refuses_to_start()
    {
        var sample = ReadSampleKeys();

        var failures = StartAndCollectFailures("Production", sample.Encryption, sample.Signing);

        Assert.Contains(failures, failure =>
        {
            return failure.Contains(EncryptionPath, StringComparison.Ordinal)
                && failure.Contains("published", StringComparison.Ordinal);
        });
        Assert.Contains(failures, failure =>
        {
            return failure.Contains(SigningPath, StringComparison.Ordinal)
                && failure.Contains("published", StringComparison.Ordinal);
        });
        Assert.Contains(failures, failure =>
        {
            return failure.Contains(SigningPath, StringComparison.Ordinal)
                && failure.Contains(EncryptionPath, StringComparison.Ordinal)
                && failure.Contains("same key", StringComparison.Ordinal);
        });
    }

    /// <summary>A published encryption key is refused, once, with its cost and without echoing it.</summary>
    [Fact]
    public void A_production_host_refuses_a_published_encryption_key_and_says_why()
    {
        var failures = StartAndCollectFailures("Production", PublishedKey, GoodSigningKey);

        var failure = Assert.Single(failures);
        Assert.Contains(EncryptionPath, failure, StringComparison.Ordinal);
        Assert.Contains("published", failure, StringComparison.Ordinal);
        Assert.Contains("openssl rand -base64 32", failure, StringComparison.Ordinal);
        Assert.Contains("re-encryption", failure, StringComparison.Ordinal);
        AssertNoKeyEchoed(failure);
    }

    /// <summary>A published signing key is refused, once, with its remedy and without echoing it.</summary>
    [Fact]
    public void A_production_host_refuses_a_published_signing_key_and_says_why()
    {
        var failures = StartAndCollectFailures("Production", GoodEncryptionKey, PublishedKey);

        var failure = Assert.Single(failures);
        Assert.Contains(SigningPath, failure, StringComparison.Ordinal);
        Assert.Contains("published", failure, StringComparison.Ordinal);
        Assert.Contains("signed out", failure, StringComparison.Ordinal);
        Assert.Contains("no data is lost", failure, StringComparison.Ordinal);
        AssertNoKeyEchoed(failure);
    }

    /// <summary>An encryption key that is printable text is refused as structured, not as published.</summary>
    [Fact]
    public void A_production_host_refuses_an_encryption_key_that_is_text()
    {
        var text = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(PassphraseText));

        var failures = StartAndCollectFailures("Production", text, GoodSigningKey);

        var failure = Assert.Single(failures);
        Assert.Contains(EncryptionPath, failure, StringComparison.Ordinal);
        Assert.Contains("too structured", failure, StringComparison.Ordinal);
        Assert.DoesNotContain("published", failure, StringComparison.Ordinal);
        Assert.DoesNotContain(PassphraseText, failure, StringComparison.Ordinal);
    }

    /// <summary>A signing key built from sixteen repeating byte values is refused as structured.</summary>
    [Fact]
    public void A_production_host_refuses_a_patterned_signing_key()
    {
        var pattern = new byte[32];
        for (var index = 0; index < pattern.Length; index++)
        {
            pattern[index] = (byte)(0x80 + (index % 16));
        }

        var failures = StartAndCollectFailures("Production", GoodEncryptionKey, Convert.ToBase64String(pattern));

        var failure = Assert.Single(failures);
        Assert.Contains(SigningPath, failure, StringComparison.Ordinal);
        Assert.Contains("too structured", failure, StringComparison.Ordinal);
    }

    /// <summary>One key used for both purposes is refused whichever setting is applied first.</summary>
    /// <param name="signingFirst">Whether the signing key is applied to the host before the encryption key.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_production_host_refuses_a_signing_key_equal_to_the_encryption_key(bool signingFirst)
    {
        var failures = StartAndCollectFailures(
            "Production",
            GoodEncryptionKey,
            GoodEncryptionKey,
            signingFirst);

        var failure = Assert.Single(failures);
        Assert.Contains(SigningPath, failure, StringComparison.Ordinal);
        Assert.Contains(EncryptionPath, failure, StringComparison.Ordinal);
        Assert.Contains("same key", failure, StringComparison.Ordinal);
        Assert.Contains("Replace the signing key", failure, StringComparison.Ordinal);
    }

    /// <summary>A signing key that embeds the encryption key hands it over, so it is refused too.</summary>
    [Fact]
    public void A_production_host_refuses_a_signing_key_that_contains_the_encryption_key()
    {
        var embedded = DerivedTestKeys.Bytes("integration-head", 16)
            .Concat(Convert.FromBase64String(GoodEncryptionKey))
            .Concat(DerivedTestKeys.Bytes("integration-tail", 16))
            .ToArray();

        var failures = StartAndCollectFailures("Production", GoodEncryptionKey, Convert.ToBase64String(embedded));

        var failure = Assert.Single(failures);
        Assert.Contains(SigningPath, failure, StringComparison.Ordinal);
        Assert.Contains("contains", failure, StringComparison.Ordinal);
    }

    /// <summary>The inverse control: two independent random keys boot in Production.</summary>
    [Fact]
    public async Task A_production_host_with_two_independent_random_keys_boots()
    {
        using var factory = CreateFactory("Production", GoodEncryptionKey, GoodSigningKey, signingFirst: false);

        // The override must have taken, or every Production case here would silently run as Testing.
        Assert.Equal("Production", factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Development and Testing keep working with the published sample.</summary>
    /// <param name="environment">An environment on the allow-list.</param>
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task Development_and_testing_hosts_boot_with_the_published_sample_keys(string environment)
    {
        using var factory = CreateFactory(environment, PublishedKey, PublishedKey, signingFirst: false);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>The allow-list is closed: every other name, near misses included, is refused.</summary>
    /// <param name="environment">An environment outside the allow-list.</param>
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Prod")]
    [InlineData("Test")]
    [InlineData("Developmentx")]
    public void Every_environment_outside_the_allow_list_refuses_the_sample_keys(string environment)
    {
        var failures = StartAndCollectFailures(environment, PublishedKey, PublishedKey);

        Assert.Contains(failures, failure =>
        {
            return failure.Contains(EncryptionPath, StringComparison.Ordinal)
                && failure.Contains("published", StringComparison.Ordinal);
        });
        Assert.Contains(failures, failure =>
        {
            return failure.Contains(SigningPath, StringComparison.Ordinal)
                && failure.Contains("published", StringComparison.Ordinal);
        });
    }

    /// <summary>Pins that the allow-list compares names without regard to case, as the framework does.</summary>
    /// <param name="environment">An allow-listed name in another case.</param>
    [Theory]
    [InlineData("testing")]
    [InlineData("DEVELOPMENT")]
    public async Task The_environment_allow_list_ignores_case(string environment)
    {
        using var factory = CreateFactory(environment, PublishedKey, PublishedKey, signingFirst: false);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>A malformed key reports its format problem alone, never the value checks.</summary>
    [Fact]
    public void A_malformed_key_in_production_reports_only_its_format_problem()
    {
        var tooShort = DerivedTestKeys.Base64("integration-short", 12);

        var failures = StartAndCollectFailures("Production", tooShort, GoodSigningKey);

        var failure = Assert.Single(failures);
        Assert.Contains("256-bit key", failure, StringComparison.Ordinal);
        Assert.DoesNotContain("published", failure, StringComparison.Ordinal);
        Assert.DoesNotContain("structured", failure, StringComparison.Ordinal);
        Assert.DoesNotContain("same key", failure, StringComparison.Ordinal);
    }

    /// <summary>The no-echo search must be able to find a key, or "not found" proves nothing.</summary>
    [Fact]
    public void The_key_echo_search_finds_a_key_that_is_present()
    {
        var planted = "a refusal that wrongly says " + PublishedKey;

        Assert.ThrowsAny<Exception>(() =>
        {
            AssertNoKeyEchoed(planted);
        });
    }

    /// <summary>Matches a <c>Name=value</c> line of the sample file.</summary>
    /// <param name="name">The setting name as the sample file writes it.</param>
    /// <returns>A regular expression whose first group is the value.</returns>
    private static Regex SampleLine(string name)
    {
        return new Regex("^" + Regex.Escape(name) + "=(.+)$", RegexOptions.Multiline, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Reads the two keys from the repository's root <c>.env.example</c>, the file an operator copies.
    /// </summary>
    /// <returns>The encryption and signing keys exactly as the file writes them.</returns>
    private static (string Encryption, string Signing) ReadSampleKeys()
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".env.example"));
        var encryption = SampleLine("Security__EncryptionKey").Match(text);
        var signing = SampleLine("Jwt__SigningKey").Match(text);

        // Positive control: a pattern that matches nothing would turn the test into a vacuous one.
        Assert.True(encryption.Success, "Security__EncryptionKey was not found in .env.example.");
        Assert.True(signing.Success, "Jwt__SigningKey was not found in .env.example.");
        return (encryption.Groups[1].Value.Trim(), signing.Groups[1].Value.Trim());
    }

    /// <summary>Walks up from the test output to the folder that holds <c>backend/Maran.sln</c>.</summary>
    /// <returns>The repository root.</returns>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "backend", "Maran.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root was not found above the test output directory.");
    }

    /// <summary>Fails when a refusal message carries the published key in any form.</summary>
    /// <param name="message">A failure message.</param>
    private static void AssertNoKeyEchoed(string message)
    {
        Assert.DoesNotContain(PublishedKey, message, StringComparison.Ordinal);
        Assert.DoesNotContain(PublishedKeyText, message, StringComparison.Ordinal);
    }

    /// <summary>Builds the test host pinned to an environment and a pair of keys.</summary>
    /// <param name="environment">The environment name to run as.</param>
    /// <param name="encryptionKey">The encryption key to configure.</param>
    /// <param name="signingKey">The signing key to configure.</param>
    /// <param name="signingFirst">Whether the signing key is applied before the encryption key.</param>
    /// <returns>The factory; the host starts when its services are first touched.</returns>
    private static WebApplicationFactory<Program> CreateFactory(
        string environment,
        string encryptionKey,
        string signingKey,
        bool signingFirst)
    {
        return new PanelTestFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            if (signingFirst)
            {
                builder.UseSetting(SigningPath, signingKey);
                builder.UseSetting(EncryptionPath, encryptionKey);
            }
            else
            {
                builder.UseSetting(EncryptionPath, encryptionKey);
                builder.UseSetting(SigningPath, signingKey);
            }
        });
    }

    /// <summary>
    /// Starts the host and returns the text of every validation failure that stopped it. One failing
    /// options type is one exception and several are an aggregate, so both are flattened.
    /// </summary>
    /// <param name="environment">The environment name to run as.</param>
    /// <param name="encryptionKey">The encryption key to configure.</param>
    /// <param name="signingKey">The signing key to configure.</param>
    /// <param name="signingFirst">Whether the signing key is applied before the encryption key.</param>
    /// <returns>The failure messages; the call fails when the host started.</returns>
    private static List<string> StartAndCollectFailures(
        string environment,
        string encryptionKey,
        string signingKey,
        bool signingFirst = false)
    {
        using var factory = CreateFactory(environment, encryptionKey, signingKey, signingFirst);

        var thrown = Record.Exception(() =>
        {
            return factory.Services;
        });

        Assert.NotNull(thrown);
        var failures = new List<string>();
        Collect(thrown, failures);
        Assert.NotEmpty(failures);
        return failures;
    }

    /// <summary>Adds the failure messages of an exception, descending into aggregates.</summary>
    /// <param name="exception">The exception the host start threw.</param>
    /// <param name="failures">Where the messages are added.</param>
    private static void Collect(Exception exception, List<string> failures)
    {
        switch (exception)
        {
            case OptionsValidationException validation:
                failures.AddRange(validation.Failures);
                break;
            case AggregateException aggregate:
                foreach (var inner in aggregate.InnerExceptions)
                {
                    Collect(inner, failures);
                }

                break;
            default:
                Assert.Fail("The host failed for a reason other than option validation: " + exception);
                break;
        }
    }
}
