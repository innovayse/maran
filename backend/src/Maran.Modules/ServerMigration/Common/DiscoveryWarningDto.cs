using Maran.Modules.ServerMigration.Domain.Enums;

namespace Maran.Modules.ServerMigration.Common;

/// <summary>Something the discovery could not read or chose not to report in full, with a sentence the administrator can read.</summary>
/// <param name="Kind">The closed kind of the warning.</param>
/// <param name="Subject">What it is about: an account, a database or a path on the source, as the source named it.</param>
/// <param name="Message">The warning in the caller's language.</param>
public sealed record DiscoveryWarningDto(DiscoveryWarningKind Kind, string Subject, string Message);
