using Maran.Modules.Databases.Domain.Enums;
using Maran.Modules.Databases.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Databases.Services;

/// <summary>Resolves the names of privileges and presets in the request's culture.</summary>
/// <remarks>
/// A service and not a mapper because it reads the request-scoped localizer, and the names are the
/// backend's to produce: the SPA holds no privilege label (rules/architecture.md).
/// </remarks>
public sealed class DatabasePrivilegeDisplayNames
{
    /// <summary>The resource-key prefix of a privilege's name.</summary>
    private const string PrivilegeKeyPrefix = "DatabasePrivilege";

    /// <summary>The resource-key prefix of a preset's name.</summary>
    private const string PresetKeyPrefix = "DatabasePrivilegePreset";

    /// <summary>The module's display-name table.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">The module's display-name table.</param>
    public DatabasePrivilegeDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>The name of a privilege in the request's culture.</summary>
    /// <param name="privilege">The privilege.</param>
    /// <returns>Its name.</returns>
    public string NameOf(DatabasePrivilege privilege)
    {
        return _displayNames[PrivilegeKeyPrefix + privilege].Value;
    }

    /// <summary>The name of a preset in the request's culture.</summary>
    /// <param name="key">The preset.</param>
    /// <returns>Its name.</returns>
    public string NameOf(DatabasePrivilegePresetKey key)
    {
        return _displayNames[PresetKeyPrefix + key].Value;
    }
}
