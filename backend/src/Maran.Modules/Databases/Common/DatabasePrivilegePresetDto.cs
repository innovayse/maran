using Maran.Modules.Databases.Domain.Enums;

namespace Maran.Modules.Databases.Common;

/// <summary>One quick fill over the privileges.</summary>
/// <param name="Key">The preset's identity.</param>
/// <param name="Name">The preset as the operator reads it, localized by the panel.</param>
/// <param name="Privileges">The privileges the preset ticks, in the order the editor lists them.</param>
public sealed record DatabasePrivilegePresetDto(
    DatabasePrivilegePresetKey Key,
    string Name,
    IReadOnlyList<DatabasePrivilege> Privileges);
