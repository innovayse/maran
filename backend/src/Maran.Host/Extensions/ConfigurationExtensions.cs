using System.Diagnostics.CodeAnalysis;
using Maran.Host.Configuration;
using Maran.Modules.Identity.Options;

namespace Maran.Host.Extensions;

/// <summary>
/// Binds and validates every options class the host owns. Validation runs at startup
/// (<c>ValidateOnStart</c>), so a misconfigured server refuses to boot instead of failing on the
/// first request that happens to touch the bad setting.
/// </summary>
public static class ConfigurationExtensions
{
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
                $"{SecurityOptions.SectionName}:{nameof(SecurityOptions.EncryptionKey)} must be a base64-encoded 256-bit key.")
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
                $"{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)} must be a base64-encoded key of at least 32 bytes.")
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

        return services;
    }
}
