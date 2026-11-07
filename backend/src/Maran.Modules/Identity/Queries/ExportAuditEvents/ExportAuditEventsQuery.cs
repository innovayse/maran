using System.Text.Json.Serialization;
using Maran.Modules.Identity.Domain.Enums;
using Maran.SharedKernel.Pagination;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Queries.ExportAuditEvents;

/// <summary>
/// Asks for a file holding the audit journal, narrowed by the same filters the journal list takes. Bound
/// straight from the query string
/// (<c>GET /api/v1/audit/export?format=csv&amp;outcome=failed&amp;from=2026-10-01T00:00:00Z</c>);
/// <see cref="ExportAuditEventsQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <remarks>
/// Not an <c>IPagedQuery</c>: an export is the whole matching window in chronological order, so it has
/// no page, no page size and no sort. The handler answers with a plan, not with the rows; the controller
/// then streams them, so a large file is never held in memory.
/// </remarks>
/// <param name="Format">The file format; JSON when none is named.</param>
/// <param name="Action">Only entries recording this machine-stable action, or <see cref="FilterValue.None"/>.</param>
/// <param name="Outcome">Only entries that ended this way, or <c>null</c> for both.</param>
/// <param name="From">Only entries at or after this instant, or <c>null</c> for no lower bound.</param>
/// <param name="To">
/// Only entries at or before this instant, or <c>null</c> for no upper bound. When both bounds are given,
/// <paramref name="From"/> must not be later than this.
/// </param>
/// <param name="Search">
/// A term matched literally inside the actor's name, the subject, the address or the viewing operator's
/// name, or <see cref="SearchTerm.None"/>.
/// </param>
/// <param name="Actor">
/// Only entries whose actor used exactly this name, compared without regard to case, or
/// <see cref="FilterValue.None"/>.
/// </param>
/// <param name="Account">Only entries recorded against this hosting account, or <c>null</c> for every entry.</param>
/// <param name="RequestedByUserId">
/// The administrator asking, from their own access token, stamped by the action. Never bound: the export
/// is journalled under this id, so a bound value would let a caller export as somebody else.
/// </param>
/// <param name="IpAddress">The caller's address, stamped by the action. Never bound.</param>
/// <param name="UserAgent">The caller's user agent, stamped by the action. Never bound.</param>
public sealed record ExportAuditEventsQuery(
    AuditExportFormat Format = AuditExportFormat.Json,
    FilterValue Action = default,
    AuditOutcome? Outcome = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    SearchTerm Search = default,
    FilterValue Actor = default,
    Guid? Account = null,
    [property: JsonIgnore][property: BindNever][BindNever] Guid RequestedByUserId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
