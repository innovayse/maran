using Maran.Host.Configuration;
using Maran.Modules.Identity.Options;

namespace Maran.Host.Cli.Services;

/// <summary>
/// The secret values this panel is configured with, as literal strings, so the redactor can remove
/// them from text that has already left the process (a journal, a config dump) even where no pattern
/// would recognise them.
/// </summary>
/// <remarks>
/// Read from the raw configuration, which is the same source the running panel binds its options from, and
/// deliberately NOT from the validated options. A verb that needs no panel secret (the module reader, the
/// OpenAPI writer) must run on a machine that has none configured, and resolving a validated option would
/// refuse it for a missing key it never uses. A scrubber only needs the values that happen to be set.
/// </remarks>
public sealed class CliSecretLiterals
{
    /// <summary>The configuration the secret settings are read from.</summary>
    private readonly IConfiguration _configuration;

    /// <summary>Creates the source from the panel's configuration.</summary>
    /// <param name="configuration">The configuration holding the database, security, token and setup sections.</param>
    public CliSecretLiterals(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>Lists the configured secret values that are actually set.</summary>
    /// <returns>The non-empty secrets: database password, encryption key, signing key, setup token.</returns>
    public IReadOnlyList<string> Current()
    {
        return new[]
            {
                Setting(DatabaseOptions.SectionName, nameof(DatabaseOptions.Password)),
                Setting(SecurityOptions.SectionName, nameof(SecurityOptions.EncryptionKey)),
                Setting(JwtOptions.SectionName, nameof(JwtOptions.SigningKey)),
                Setting(SetupOptions.SectionName, nameof(SetupOptions.Token)),
            }
            .Where(value => { return !string.IsNullOrWhiteSpace(value); })
            .Select(value => { return value!; })
            .ToList();
    }

    /// <summary>Reads one setting without validating it.</summary>
    /// <param name="section">The configuration section.</param>
    /// <param name="key">The key inside the section.</param>
    /// <returns>The value, or null when it is not set.</returns>
    private string? Setting(string section, string key)
    {
        return _configuration[$"{section}:{key}"];
    }
}
