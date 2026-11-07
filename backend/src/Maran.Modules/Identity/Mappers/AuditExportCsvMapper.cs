using System.Globalization;
using Maran.Modules.Identity.Models;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Identity.Mappers;

/// <summary>
/// Writes an <see cref="AuditExportRow"/> as one CSV record, and the header that names its columns.
/// RFC 4180 with CRLF line ends; every field goes through <see cref="CsvField"/>, which quotes it and
/// neutralises a leading spreadsheet-formula character.
/// </summary>
/// <remarks>
/// <b>The subject and the user agent are text an attacker typed</b> (a claimed name on a failed sign-in,
/// a request header), and the file is opened in a spreadsheet by the administrator who downloaded it, so
/// no field is written except through <see cref="CsvField.Of"/>. The header names are the DTO's property
/// names in camel case, which is also what the JSON export writes; a test holds the two together.
/// </remarks>
public static class AuditExportCsvMapper
{
    /// <summary>The column names, in the order <see cref="Line"/> writes the fields.</summary>
    private const string HeaderText =
        "id,occurredAt,actorUserId,actorUsername,impersonatorUsername,action,outcome,subject,ipAddress,userAgent,"
        + "accountId,correlationId,chainIndex,rowHash";

    /// <summary>The record separator RFC 4180 asks for.</summary>
    private const string RecordEnd = "\r\n";

    /// <summary>The header record.</summary>
    /// <returns>The column names joined by commas, ending with CRLF.</returns>
    public static string Header()
    {
        return HeaderText + RecordEnd;
    }

    /// <summary>One row as a CSV record.</summary>
    /// <param name="row">The row to write.</param>
    /// <returns>The fields joined by commas, ending with CRLF.</returns>
    public static string Line(AuditExportRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        string[] fields =
        [
            CsvField.Of(row.Id.ToString()),
            CsvField.Of(row.OccurredAt),
            CsvField.Of(row.ActorUserId?.ToString()),
            CsvField.Of(row.ActorUsername),
            CsvField.Of(row.ImpersonatorUsername),
            CsvField.Of(row.Action),
            CsvField.Of(row.Outcome),
            CsvField.Of(row.Subject),
            CsvField.Of(row.IpAddress),
            CsvField.Of(row.UserAgent),
            CsvField.Of(row.AccountId?.ToString()),
            CsvField.Of(row.CorrelationId),
            CsvField.Of(row.ChainIndex?.ToString(CultureInfo.InvariantCulture)),
            CsvField.Of(row.RowHash),
        ];

        return string.Join(',', fields) + RecordEnd;
    }
}
