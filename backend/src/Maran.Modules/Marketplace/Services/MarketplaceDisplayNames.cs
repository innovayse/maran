using System.Globalization;
using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// Produces the text the Marketplace screen shows in the caller's language: the sentences behind error
/// codes, the entitlement and tier names, and the lines saying what the panel sends. The backend owns every
/// word the screen renders (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
public sealed class MarketplaceDisplayNames
{
    /// <summary>The key prefix of the entitlement state names in <c>DisplayNames</c>.</summary>
    private const string EntitlementKeyPrefix = "Entitlement";

    /// <summary>The key prefix of the tier names in <c>DisplayNames</c>.</summary>
    private const string TierKeyPrefix = "MarketplaceTier";

    /// <summary>The keys of the lines saying what the panel sends the marketplace, in the order they are shown.</summary>
    private static readonly string[] SentItemKeys =
    [
        "SentItemPanelVersion",
        "SentItemChannel",
        "SentItemLanguage",
        "SentItemLeaseCredential",
        "SentItemInstalledModules",
        "SentItemNothingElse",
    ];

    /// <summary>The panel-wide table of error sentences, resolved by code.</summary>
    private readonly IErrorTextProvider _errors;

    /// <summary>This module's names.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>The two-letter language of the current request, which the marketplace localizes its own text in.</summary>
    public static string Locale
    {
        get
        {
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        }
    }

    /// <summary>Creates the display names.</summary>
    /// <param name="errors">The panel-wide table of error sentences, resolved by code.</param>
    /// <param name="names">This module's names.</param>
    public MarketplaceDisplayNames(IErrorTextProvider errors, IStringLocalizer<DisplayNames> names)
    {
        _errors = errors;
        _names = names;
    }

    /// <summary>The sentence behind an error code, in the caller's language.</summary>
    /// <param name="error">The error.</param>
    /// <returns>The sentence.</returns>
    public string Sentence(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return _errors.Resolve(error.Code);
    }

    /// <summary>The sentence behind an error code, or null when there is no error.</summary>
    /// <param name="error">The error, or null.</param>
    /// <returns>The sentence, or null.</returns>
    public string? SentenceOrNull(Error? error)
    {
        return error is null ? null : Sentence(error);
    }

    /// <summary>The name of an entitlement state.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The name.</returns>
    public string EntitlementName(EntitlementState state)
    {
        return _names[EntitlementKeyPrefix + state].Value;
    }

    /// <summary>The name of a licence tier as the service spells it; the spelling itself when this panel has no name for it.</summary>
    /// <param name="tier">The tier as the service names it.</param>
    /// <returns>The name.</returns>
    public string TierName(string tier)
    {
        var name = _names[TierKeyPrefix + tier];
        return name.ResourceNotFound ? tier : name.Value;
    }

    /// <summary>The lines saying what the panel sends the marketplace, in the caller's language.</summary>
    /// <returns>The lines, in the order they are shown.</returns>
    public IReadOnlyList<string> SentItems()
    {
        return SentItemKeys.Select(key => { return _names[key].Value; }).ToList();
    }
}
