using Maran.SharedKernel.Security;

namespace Maran.Modules.ServerMigration.Common;

/// <summary>A pull grant just issued: the only time its token is ever shown.</summary>
/// <remarks>
/// The token is a <see cref="SensitiveString"/>, so this record's printed form is a mask; it reads and writes as
/// the plain JSON string like every credential the panel returns once. Only its digest is stored.
/// </remarks>
/// <param name="Id">The grant's id, used to revoke it.</param>
/// <param name="ExportId">The export the grant opens.</param>
/// <param name="Token">The bearer token the receiving panel presents. Shown once.</param>
/// <param name="AllowedFrom">The address range the pull must come from, or <c>null</c> for any.</param>
/// <param name="ExpiresAt">When the grant stops working.</param>
public sealed record ExportGrantDto(
    Guid Id,
    Guid ExportId,
    SensitiveString Token,
    string? AllowedFrom,
    DateTimeOffset ExpiresAt);
