using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Queries.GetAccountImportReport;

/// <summary>
/// The dry run of an import: every module judges its section against this server and nothing changes (issue
/// #127).
/// </summary>
/// <param name="PlanId">The plan the administrator would create the account against.</param>
/// <param name="ImportId">The import, taken from the route and never from the query string or a body.</param>
public sealed record GetAccountImportReportQuery(
    Guid PlanId,
    [property: JsonIgnore][property: BindNever][BindNever] Guid ImportId = default);
