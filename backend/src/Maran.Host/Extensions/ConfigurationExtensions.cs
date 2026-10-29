using System.Diagnostics.CodeAnalysis;
using Maran.Host.Configuration;
using Maran.Modules.Identity.Options;
using Maran.SharedKernel.Security;

namespace Maran.Host.Extensions;

/// <summary>
/// Binds and validates every options class the host owns. Validation runs at startup
/// (<c>ValidateOnStart</c>), so a misconfigured server refuses to boot instead of failing on the
/// first request that happens to touch the bad setting.
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// The environment the test hosts run as. With <see cref="HostEnvironmentEnvExtensions.IsDevelopment"/>
    /// it is the whole allow-list of environments that may run with a published or weak key.
    /// </summary>
    private const string TestingEnvironmentName = "Testing";

    /// <summary>The configuration path of the encryption key, as an operator writes it.</summary>
    private const string EncryptionKeySetting =
        $"{SecurityOptions.SectionName}:{nameof(SecurityOptions.EncryptionKey)}";

    /// <summary>The configuration path of the signing key, as an operator writes it.</summary>
    private const string SigningKeySetting = $"{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)}";

    /// <summary>The sentence appended to every key refusal, saying where refusals apply.</summary>
    private const string RefusedOutsideDevelopment =
        "Outside the Development and Testing environments the panel refuses to run with it.";

    /// <summary>
    /// The remedy for a refused encryption key, with its cost. Replacing this key is not free and
    /// the message must not pretend it is: there is no re-encryption tool, so what was encrypted
    /// under the old key cannot be read under the new one.
    /// </summary>
    private const string EncryptionKeyRemedy =
        "Generate a new key with 'openssl rand -base64 32', knowing what that costs: the panel has no "
        + "re-encryption tool, so every value encrypted under the old key (stored two-factor secrets, the "
        + "ACME account key, the SMTP password) can no longer be read and must be set up again. A panel "
        + "that has already run with this key should treat those values as exposed regardless.";

    /// <summary>
    /// The remedy for a refused signing key. Replacing it loses no data: it only invalidates every
    /// access token signed under the old key, forged ones included.
    /// </summary>
    private const string SigningKeyRemedy =
        "Replace it with a new key from 'openssl rand -base64 32': every user is signed out, and no "
        + "data is lost.";

    /// <summary>Refusal of an encryption key equal to a development key this repository publishes.</summary>
    private const string EncryptionKeyPublishedMessage =
        EncryptionKeySetting + " is a development key published in this product's source code, so "
        + "anyone can decrypt the secrets the panel stores with it. " + RefusedOutsideDevelopment + " "
        + EncryptionKeyRemedy;

    /// <summary>Refusal of an encryption key too structured to have come from a random generator.</summary>
    private const string EncryptionKeyLowEntropyMessage =
        EncryptionKeySetting + " decodes to bytes too structured to be a random key (text such as a "
        + "passphrase or a hex string, or a short repeated pattern), so a single database dump is "
        + "enough to guess it offline. " + RefusedOutsideDevelopment + " " + EncryptionKeyRemedy;

    /// <summary>Refusal of a signing key equal to a development key this repository publishes.</summary>
    private const string SigningKeyPublishedMessage =
        SigningKeySetting + " is a development key published in this product's source code, so "
        + "anyone can sign an administrator's access token with it. " + RefusedOutsideDevelopment + " "
        + SigningKeyRemedy;

    /// <summary>Refusal of a signing key too structured to have come from a random generator.</summary>
    private const string SigningKeyLowEntropyMessage =
        SigningKeySetting + " decodes to bytes too structured to be a random key (text such as a "
        + "passphrase or a hex string, or a short repeated pattern), so a single access token is "
        + "enough to guess it offline. " + RefusedOutsideDevelopment + " " + SigningKeyRemedy;

    /// <summary>
    /// Refusal of a signing key that is, or contains, the encryption key. The remedy names the
    /// signing key on purpose: replacing it loses nothing, replacing the encryption key loses data.
    /// </summary>
    private const string SigningKeyEqualsEncryptionKeyMessage =
        SigningKeySetting + " is the same key as " + EncryptionKeySetting + " (or contains it), so "
        + "whoever obtains either one holds both. " + RefusedOutsideDevelopment + " Replace the signing "
        + "key, not the encryption key, with a new key from 'openssl rand -base64 32': every user is "
        + "signed out, and no data is lost.";

    /// <summary>Registers all panel options with data-annotation and custom validation.</summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configuration">Configuration the options bind from.</param>
    /// <returns>The same collection, for chaining.</returns>
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The parameter stays IConfiguration: this is the host's composition surface, and "
            + "binding it to the concrete builder type would leak a construction detail into every caller.")]
    public static IServiceCollection AddPanelConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AgentOptions>()
            .Bind(configuration.GetSection(AgentOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .ValidateDataAnnotations()
            // Data annotations cannot measure the decoded key, so the decoded length is checked
            // here: a short or malformed key must stop the boot, never reach encryption at runtime.
            .Validate(
                options =>
                {
                    return options.HasValidEncryptionKey();
                },
                $"{EncryptionKeySetting} must be a base64-encoded 256-bit key.")
            // The key's VALUE, judged by SecretKeyPolicy: a published development key or a structured
            // one is refused everywhere but Development and Testing. Each refusal names the setting,
            // the problem and the remedy, and never a byte of the key.
            .Validate<IHostEnvironment>(
                (options, environment) =>
                {
                    return AdmitsDevelopmentKeys(environment)
                        || !SecretKeyPolicy.IsPublishedDevelopmentKey(options.EncryptionKey);
                },
                EncryptionKeyPublishedMessage)
            // A published key is also structured; it is reported once, as published. A key of the wrong
            // format is reported once, as malformed, never also as structured.
            .Validate<IHostEnvironment>(
                (options, environment) =>
                {
                    return AdmitsDevelopmentKeys(environment)
                        || !options.HasValidEncryptionKey()
                        || SecretKeyPolicy.IsPublishedDevelopmentKey(options.EncryptionKey)
                        || !SecretKeyPolicy.IsLowEntropy(options.EncryptionKey);
                },
                EncryptionKeyLowEntropyMessage)
            .ValidateOnStart();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            // Same reasoning as the encryption key above: an unusable signing key must stop the
            // boot, not surface as every login failing with an unexplained 500.
            .Validate(
                options =>
                {
                    return options.HasValidSigningKey();
                },
                $"{SigningKeySetting} must be a base64-encoded key of at least 32 bytes.")
            .Validate<IHostEnvironment>(
                (options, environment) =>
                {
                    return AdmitsDevelopmentKeys(environment)
                        || !SecretKeyPolicy.IsPublishedDevelopmentKey(options.SigningKey);
                },
                SigningKeyPublishedMessage)
            .Validate<IHostEnvironment>(
                (options, environment) =>
                {
                    return AdmitsDevelopmentKeys(environment)
                        || !options.HasValidSigningKey()
                        || SecretKeyPolicy.IsPublishedDevelopmentKey(options.SigningKey)
                        || !SecretKeyPolicy.IsLowEntropy(options.SigningKey);
                },
                SigningKeyLowEntropyMessage)
            // The one check that needs both keys. It reads the encryption key from configuration
            // rather than from IOptions<SecurityOptions>: resolving those options here would run
            // their validators inside this one, and a refusal of the encryption key would surface
            // as a failure of the signing key. Comparing on decoded bytes, it runs at start whichever
            // key was configured first.
            .Validate<IHostEnvironment, IConfiguration>(
                (options, environment, panelConfiguration) =>
                {
                    return AdmitsDevelopmentKeys(environment)
                        || !SecretKeyPolicy.AreSameKey(options.SigningKey, panelConfiguration[EncryptionKeySetting]);
                },
                SigningKeyEqualsEncryptionKeyMessage)
            .ValidateOnStart();

        // No validation callback and no [Required]: an absent token is the normal state of a panel
        // that has finished its setup, and it must not stop the boot.
        services.AddOptions<SetupOptions>()
            .Bind(configuration.GetSection(SetupOptions.SectionName));

        // Also no [Required], for a different reason: a panel on TCP (development, and a server
        // whose installer has not been re-run) never consults the uid, so demanding it would fail
        // a boot with nothing wrong with it. Absence is refused where it means something instead —
        // ListenSocketGuard stops a panel that bound a socket without one, and PanelPeerPolicy
        // permits nobody until one is set.
        services.AddOptions<ReverseProxyOptions>()
            .Bind(configuration.GetSection(ReverseProxyOptions.SectionName));

        // No [Required] either: a panel installed before the setting existed has none, and absence
        // means "reserve only the static set" (PanelHostDirectory), never a refused boot.
        services.AddOptions<PanelHostOptions>()
            .Bind(configuration.GetSection(PanelHostOptions.SectionName));

        return services;
    }

    /// <summary>
    /// Whether this host may run with a published or structurally weak key, or with one key used
    /// for both purposes: only in Development and in Testing, where the test hosts run with the
    /// published sample.
    /// </summary>
    /// <remarks>
    /// An allow-list, not "refuse in Production": an unset <c>ASPNETCORE_ENVIRONMENT</c> (which .NET
    /// reads as Production, and which is what the installed service runs with), Staging and a
    /// misspelt name are all refused, because the server this exists for is one nobody configured
    /// carefully. The environment name comes from the same operator-owned <c>panel.env</c> as the
    /// keys, so whoever can set it to Development can already replace the key itself; the allowance
    /// gives an attacker nothing they did not have.
    /// </remarks>
    /// <param name="environment">The host's environment.</param>
    /// <returns>True when the key-value refusals do not apply.</returns>
    private static bool AdmitsDevelopmentKeys(IHostEnvironment environment)
    {
        return environment.IsDevelopment() || environment.IsEnvironment(TestingEnvironmentName);
    }
}
