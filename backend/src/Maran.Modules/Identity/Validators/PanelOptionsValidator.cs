using Maran.Modules.Identity.Options;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Validators;

/// <summary>
/// Holds <see cref="PanelOptions.PublicUrl"/> to the panel's one definition of a public origin at
/// startup, so a malformed value stops the boot instead of producing a credential link nobody can
/// open — or one that opens somewhere else — and says out loud when no value is configured.
/// </summary>
/// <remarks>
/// <para>
/// <b>Empty is accepted and warned about; malformed is refused.</b> An empty value is a supported
/// configuration (a development host, a panel installed before the setting existed): mail then
/// carries the bare token. That is safe but easy to overlook, so the validator — which the options
/// system runs when the options are validated on start, and again when a service first reads them
/// through <c>IOptions&lt;T&gt;</c>, because each of the two builds its own copy — logs one warning
/// naming the setting every time it runs.
/// A present value that is not an origin (<see cref="PublicUrlRule"/>) is the operator's mistake and
/// the boot stops, because a link built from it would be sent in the panel's name.
/// </para>
/// <para>
/// The failure message never repeats the value: it is operator-supplied text, and the log is the
/// wrong place to echo bytes whose only known property is that they were not an address.
/// </para>
/// </remarks>
public sealed class PanelOptionsValidator : IValidateOptions<PanelOptions>
{
    /// <summary>Pre-compiled log delegate for a panel that starts with no public address.</summary>
    private static readonly Action<ILogger, string, Exception?> LogNoPublicUrl =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(PanelOptionsValidator)),
            "{Setting} is not set, so the links in invitation and password-reset mails carry no address "
            + "and cannot be clicked. Set it to the address this panel is reached at, for example "
            + "https://panel.example.com:8443");

    /// <summary>Where the missing-setting warning is written.</summary>
    private readonly ILogger<PanelOptionsValidator> _logger;

    /// <summary>Creates the validator.</summary>
    /// <param name="logger">Where the missing-setting warning is written.</param>
    public PanelOptionsValidator(ILogger<PanelOptionsValidator> logger)
    {
        _logger = logger;
    }

    /// <summary>Validates the bound options.</summary>
    /// <param name="name">The named options instance being validated; unused, the section is unnamed.</param>
    /// <param name="options">The bound settings.</param>
    /// <returns>Success, or a failure naming the setting and the shape it must have.</returns>
    public ValidateOptionsResult Validate(string? name, PanelOptions options)
    {
        var setting = $"{PanelOptions.SectionName}:{nameof(PanelOptions.PublicUrl)}";

        if (string.IsNullOrWhiteSpace(options.PublicUrl))
        {
            LogNoPublicUrl(_logger, setting, null);
            return ValidateOptionsResult.Success;
        }

        if (!PublicUrlRule.IsPublicUrl(options.PublicUrl))
        {
            return ValidateOptionsResult.Fail(
                $"{setting} must be an https origin: a DNS name or IP address, an optional port from 1 to "
                + "65535, and no user information, path, query, fragment, whitespace or control characters "
                + "(for example https://panel.example.com:8443).");
        }

        return ValidateOptionsResult.Success;
    }
}
