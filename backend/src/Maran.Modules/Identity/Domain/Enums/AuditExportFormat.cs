namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>The file formats the audit journal can be exported in.</summary>
public enum AuditExportFormat
{
    /// <summary>RFC 4180 comma-separated values, neutralised against spreadsheet formulas; for people who open it in a spreadsheet.</summary>
    Csv = 1,

    /// <summary>One JSON array of objects; the lossless format, for programs.</summary>
    Json = 2,
}
