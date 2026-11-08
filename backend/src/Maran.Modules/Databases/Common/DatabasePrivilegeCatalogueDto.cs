namespace Maran.Modules.Databases.Common;

/// <summary>The closed privilege set and the presets over it: everything the privileges editor renders.</summary>
/// <param name="Privileges">Every privilege that can be granted, in the order the editor lists them.</param>
/// <param name="Presets">The quick fills over those privileges.</param>
public sealed record DatabasePrivilegeCatalogueDto(
    IReadOnlyList<DatabasePrivilegeDto> Privileges,
    IReadOnlyList<DatabasePrivilegePresetDto> Presets);
