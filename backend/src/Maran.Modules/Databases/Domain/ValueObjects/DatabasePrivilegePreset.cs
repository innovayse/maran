using Maran.Modules.Databases.Domain.Enums;

namespace Maran.Modules.Databases.Domain.ValueObjects;

/// <summary>One named privilege set the users screen offers as a quick fill.</summary>
/// <param name="Key">Which preset this is; the screen's heading comes from the key, localized by the backend.</param>
/// <param name="Privileges">The privileges the preset fills in, in the order the screen lists them.</param>
public sealed record DatabasePrivilegePreset(DatabasePrivilegePresetKey Key, IReadOnlyList<DatabasePrivilege> Privileges);
