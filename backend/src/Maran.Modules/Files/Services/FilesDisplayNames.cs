using Maran.Modules.Files.Domain.Enums;
using Maran.Modules.Files.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Files.Services;

/// <summary>
/// Names the module's closed vocabularies in the request's culture, so a DTO carries the word beside
/// the value and the SPA holds no list of them.
/// </summary>
public sealed class FilesDisplayNames
{
    /// <summary>The resource-key prefix of an entry kind's name.</summary>
    private const string KindPrefix = "FileEntryKind";

    /// <summary>The localizer over <c>Resources/DisplayNames</c>, resolving the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the name provider.</summary>
    /// <param name="displayNames">The localizer over the module's display names.</param>
    public FilesDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>The name of an entry kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The name in the request's culture.</returns>
    public string KindOf(FileEntryKind kind)
    {
        return _displayNames[KindPrefix + kind].Value;
    }
}
