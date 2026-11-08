using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Domain.Policies;
using Maran.Modules.Databases.Services;

namespace Maran.Modules.Databases.Queries.ListDatabasePrivilegePresets;

/// <summary>Handles <see cref="ListDatabasePrivilegePresetsQuery"/> from <see cref="DatabasePrivilegePresetCatalog"/>.</summary>
public sealed class ListDatabasePrivilegePresetsQueryHandler
{
    /// <summary>Resolves privilege and preset names in the request's culture.</summary>
    private readonly DatabasePrivilegeDisplayNames _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="names">Resolves privilege and preset names in the request's culture.</param>
    public ListDatabasePrivilegePresetsQueryHandler(DatabasePrivilegeDisplayNames names)
    {
        _names = names;
    }

    /// <summary>Builds the catalogue.</summary>
    /// <param name="query">The request; it carries nothing.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The privileges in the editor's order, and the presets.</returns>
    public Task<Result<DatabasePrivilegeCatalogueDto>> HandleAsync(
        ListDatabasePrivilegePresetsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var privileges = DatabasePrivilegeSets.All
            .Select(privilege => { return new DatabasePrivilegeDto(privilege, _names.NameOf(privilege)); })
            .ToList();
        var presets = DatabasePrivilegePresetCatalog.All
            .Select(preset => { return new DatabasePrivilegePresetDto(preset.Key, _names.NameOf(preset.Key), preset.Privileges); })
            .ToList();

        return Task.FromResult(
            Result<DatabasePrivilegeCatalogueDto>.Ok(new DatabasePrivilegeCatalogueDto(privileges, presets)));
    }
}
