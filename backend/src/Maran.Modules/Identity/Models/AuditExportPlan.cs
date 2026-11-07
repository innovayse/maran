using Maran.Modules.Identity.Domain.Enums;

namespace Maran.Modules.Identity.Models;

/// <summary>
/// What an authorised, journalled export of the audit journal is going to write: the handler's answer and
/// the writer's whole input. It holds no row.
/// </summary>
/// <param name="Format">The file format.</param>
/// <param name="Filter">The conditions the rows must satisfy.</param>
/// <param name="RowCount">
/// How many rows matched when the plan was made, never more than <c>AuditExportLimits.MaxRows</c>. The
/// writer stops after this many rows, so entries written while the file streams cannot make it larger than
/// the size the handler checked and journalled.
/// </param>
/// <param name="FileName">The attachment's file name, built from the clock and the format only.</param>
/// <param name="ContentType">The media type of the file.</param>
public sealed record AuditExportPlan(
    AuditExportFormat Format,
    AuditEventFilter Filter,
    long RowCount,
    string FileName,
    string ContentType);
