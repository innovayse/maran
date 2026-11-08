namespace Maran.Modules.Databases.Queries.ListDatabasePrivilegePresets;

/// <summary>
/// Reads the closed privilege set and the presets over it, with their names in the caller's language:
/// everything the privileges editor renders, none of which the SPA holds itself.
/// </summary>
public sealed record ListDatabasePrivilegePresetsQuery;
