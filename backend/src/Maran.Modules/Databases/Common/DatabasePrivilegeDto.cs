using Maran.Modules.Databases.Domain.Enums;

namespace Maran.Modules.Databases.Common;

/// <summary>One privilege the editor offers.</summary>
/// <param name="Key">The privilege's wire key, which a grant sends back.</param>
/// <param name="Name">The privilege as the operator reads it, localized by the panel.</param>
public sealed record DatabasePrivilegeDto(DatabasePrivilege Key, string Name);
