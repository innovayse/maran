namespace Maran.Modules.Identity.Models;

/// <summary>
/// One journal row as an export file carries it: fourteen machine-neutral columns, not localized, in the
/// order the CSV header names them. The JSON export writes the same names in camel case.
/// </summary>
/// <remarks>
/// A row, not a <c>*Dto</c>: it is written into a downloaded file for scripts and archives and is never
/// returned by a screen's endpoint, so no operator reads it through the SPA. The columns stay machine-stable
/// on purpose (<c>Action</c> is the code a script greps for), and a display name beside it would add a
/// fifteenth column to a contract that is pinned at fourteen. The display-name law
/// (<c>DisplayNameLawTests</c>) judges the DTOs a screen renders, which is why this is not one.
/// </remarks>
/// <param name="Id">The entry's identity.</param>
/// <param name="OccurredAt">When it happened, ISO-8601 in UTC with microseconds (the database's precision).</param>
/// <param name="ActorUserId">Who did it; null when the actor could not be identified.</param>
/// <param name="ActorUsername">The name the actor used.</param>
/// <param name="ImpersonatorUsername">The operator who was viewing as the actor; null when there was none.</param>
/// <param name="Action">The machine-stable action name.</param>
/// <param name="Outcome"><c>succeeded</c> or <c>failed</c>.</param>
/// <param name="Subject">What it was attempted on.</param>
/// <param name="IpAddress">Where it came from.</param>
/// <param name="UserAgent">What client it came from.</param>
/// <param name="AccountId">The hosting account the entry is about; null when it records none.</param>
/// <param name="CorrelationId">The request's correlation id; null when there was none.</param>
/// <param name="ChainIndex">The entry's position in the tamper-evidence chain; null while it is not sealed yet.</param>
/// <param name="RowHash">The entry's chain hash, lower-case hexadecimal; null while it is not sealed yet.</param>
public sealed record AuditExportRow(
    Guid Id,
    string OccurredAt,
    Guid? ActorUserId,
    string ActorUsername,
    string? ImpersonatorUsername,
    string Action,
    string Outcome,
    string Subject,
    string IpAddress,
    string UserAgent,
    Guid? AccountId,
    string? CorrelationId,
    long? ChainIndex,
    string? RowHash);
