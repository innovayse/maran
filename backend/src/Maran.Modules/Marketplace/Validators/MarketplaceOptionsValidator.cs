using Maran.Modules.Marketplace.Options;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Marketplace.Validators;

/// <summary>
/// Holds <see cref="MarketplaceOptions"/> to what the marketplace conversation needs: an https address with
/// a host and nothing else in it, absolute paths, a plain channel name, and every bound positive and under
/// its ceiling.
/// </summary>
/// <remarks>
/// <para>
/// <b>Run on first use, not at start-up.</b> It is registered as an <see cref="IValidateOptions{TOptions}"/>
/// and the options carry no <c>ValidateOnStart</c>, so a server that never switches the marketplace on boots
/// whatever its configuration holds; the first <c>IOptions&lt;MarketplaceOptions&gt;.Value</c> read throws
/// <see cref="OptionsValidationException"/>, and a caller that wants a verdict instead of an exception
/// calls <see cref="Validate"/> itself.
/// </para>
/// <para>
/// <b>What the address must be.</b> An absolute <c>https</c> URL with a host, no user information
/// (credentials in a URL end up in logs), no query and no fragment, and no whitespace or control
/// character anywhere in the text (rules/security.md item 4). Plain <c>http</c> is refused with no
/// exception for loopback: the address is the host allow-list, and an allow-list entry that does not
/// authenticate the server is not one.
/// </para>
/// </remarks>
public sealed class MarketplaceOptionsValidator : IValidateOptions<MarketplaceOptions>
{
    /// <summary>The longest release channel name accepted.</summary>
    private const int ChannelMaxLength = 32;

    /// <summary>Validates the bound options.</summary>
    /// <param name="name">The named options instance being validated; unused, the section is unnamed.</param>
    /// <param name="options">The bound settings.</param>
    /// <returns>Success, or a failure naming every setting that is wrong and what is wrong with it.</returns>
    public ValidateOptionsResult Validate(string? name, MarketplaceOptions options)
    {
        var failures = new List<string>();

        if (!IsAcceptableBaseUrl(options.BaseUrl))
        {
            failures.Add(Describe(
                nameof(MarketplaceOptions.BaseUrl),
                "must be an absolute https URL with a host, no user information, no query, no fragment, "
                + "and no whitespace or control characters"));
        }

        if (!IsAbsolutePath(options.MetadataKeyPath))
        {
            failures.Add(Describe(nameof(MarketplaceOptions.MetadataKeyPath), "must be an absolute path"));
        }

        if (!IsAbsolutePath(options.PackageDirectory))
        {
            failures.Add(Describe(nameof(MarketplaceOptions.PackageDirectory), "must be an absolute path"));
        }

        if (!IsAcceptableChannel(options.Channel))
        {
            failures.Add(Describe(
                nameof(MarketplaceOptions.Channel),
                $"must be 1 to {ChannelMaxLength} lower-case letters, digits or hyphens, starting with a letter"));
        }

        AddBound(failures, nameof(MarketplaceOptions.RequestTimeoutSeconds), options.RequestTimeoutSeconds,
            MarketplaceOptions.RequestTimeoutSecondsCeiling);
        AddBound(failures, nameof(MarketplaceOptions.MaxCatalogueBytes), options.MaxCatalogueBytes,
            MarketplaceOptions.CatalogueBytesCeiling);
        AddBound(failures, nameof(MarketplaceOptions.MaxGrantBytes), options.MaxGrantBytes,
            MarketplaceOptions.GrantBytesCeiling);
        AddBound(failures, nameof(MarketplaceOptions.MaxPackageBytes), options.MaxPackageBytes,
            MarketplaceOptions.PackageBytesCeiling);
        AddBound(failures, nameof(MarketplaceOptions.CatalogueMaxAgeHours), options.CatalogueMaxAgeHours,
            MarketplaceOptions.CatalogueMaxAgeHoursCeiling);
        AddBound(failures, nameof(MarketplaceOptions.CacheMinutes), options.CacheMinutes,
            MarketplaceOptions.CacheMinutesCeiling);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Tells whether an address is an https URL carrying nothing a URL should not.</summary>
    /// <param name="value">The configured address.</param>
    /// <returns><see langword="true"/> when the value is acceptable.</returns>
    private static bool IsAcceptableBaseUrl(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                return false;
            }
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Length > 0
            && uri.UserInfo.Length == 0
            && uri.Query.Length == 0
            && uri.Fragment.Length == 0
            && !value.Contains('?', StringComparison.Ordinal)
            && !value.Contains('#', StringComparison.Ordinal);
    }

    /// <summary>Tells whether a value is a plain absolute path.</summary>
    /// <param name="value">The configured path.</param>
    /// <returns><see langword="true"/> when it starts at the root and holds no control character.</returns>
    private static bool IsAbsolutePath(string? value)
    {
        return !string.IsNullOrEmpty(value)
            && value[0] == '/'
            && !value.Any(char.IsControl);
    }

    /// <summary>Tells whether a channel name is plain.</summary>
    /// <param name="value">The configured channel.</param>
    /// <returns><see langword="true"/> when it is a short lower-case word.</returns>
    private static bool IsAcceptableChannel(string? value)
    {
        return !string.IsNullOrEmpty(value)
            && value.Length <= ChannelMaxLength
            && value[0] is >= 'a' and <= 'z'
            && value.All(character =>
            {
                return character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-';
            });
    }

    /// <summary>Adds a failure unless a numeric setting is positive and within its ceiling.</summary>
    /// <param name="failures">The failures collected so far.</param>
    /// <param name="setting">The setting's name.</param>
    /// <param name="value">Its value.</param>
    /// <param name="ceiling">The largest value it may take.</param>
    private static void AddBound(List<string> failures, string setting, long value, long ceiling)
    {
        if (value < 1 || value > ceiling)
        {
            failures.Add(Describe(setting, $"must be between 1 and {ceiling}"));
        }
    }

    /// <summary>Spells one failure with the setting's full configuration key.</summary>
    /// <param name="setting">The setting's name.</param>
    /// <param name="problem">What is wrong with it.</param>
    /// <returns>The failure text.</returns>
    private static string Describe(string setting, string problem)
    {
        return $"{MarketplaceOptions.SectionName}:{setting} {problem}.";
    }
}
