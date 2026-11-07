using System.Globalization;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Models;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Identity.Domain.Policies;

/// <summary>
/// Spells the audit subject of an export entry, so the journal records WHAT left the panel: the format,
/// the filters and the number of rows the export was planned for.
/// </summary>
/// <remarks>
/// <para>
/// The row count is the <b>intended</b> count, not the delivered one: the entry is written before the file
/// streams, so that a client that disconnects mid-download still leaves the fact of the export behind.
/// </para>
/// <para>
/// Only filters that were given appear, so an export of the whole journal reads as just a format and a
/// count. The search term, the one free-text filter, comes last so its content cannot be mistaken for
/// another field. The result is fitted to the column with <see cref="ColumnText"/> on a code point
/// boundary, because the actor and search filters are caller-chosen text.
/// </para>
/// </remarks>
public static class AuditExportSubjectText
{
    /// <summary>Builds <c>format=csv rows=12 action=LoginFailed outcome=failed ... search=alice</c>.</summary>
    /// <param name="format">The file format.</param>
    /// <param name="filter">The conditions the rows must satisfy.</param>
    /// <param name="rowCount">How many rows the export is planned to hold.</param>
    /// <returns>The subject text recorded with the entry, at most <see cref="AuditEvent.SubjectMaxLength"/> characters.</returns>
    public static string Of(AuditExportFormat format, AuditEventFilter filter, long rowCount)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var parts = new List<string>
        {
            "format=" + format.ToString().ToLowerInvariant(),
            "rows=" + rowCount.ToString(CultureInfo.InvariantCulture),
        };

        if (!filter.Action.IsNone)
        {
            parts.Add("action=" + filter.Action.Value);
        }

        if (filter.Outcome is { } outcome)
        {
            parts.Add("outcome=" + outcome.ToString().ToLowerInvariant());
        }

        if (filter.From is { } from)
        {
            parts.Add("from=" + from.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        }

        if (filter.To is { } to)
        {
            parts.Add("to=" + to.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        }

        if (!filter.Actor.IsNone)
        {
            parts.Add("actor=" + filter.Actor.Value);
        }

        if (filter.AccountId is { } accountId)
        {
            parts.Add("account=" + accountId);
        }

        if (!filter.Search.IsNone)
        {
            parts.Add("search=" + filter.Search.Value);
        }

        return ColumnText.Fit(string.Join(' ', parts), AuditEvent.SubjectMaxLength);
    }
}
