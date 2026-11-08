using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Backups.Queries.GetMyBackupSchedule;

/// <summary>Reads the signed-in customer's own backup schedule (issue #147).</summary>
/// <remarks>
/// It takes no account from the request: asking about another account's schedule is not a question
/// this surface can express. The administrator's read, which names an account, is a different query
/// on a different endpoint.
/// </remarks>
/// <param name="AccountId">
/// The caller's own account. Established by the server from the signed-in caller and stamped by the
/// action; never bound from the request.
/// </param>
public sealed record GetMyBackupScheduleQuery(
    [property: JsonIgnore][property: BindNever][BindNever] Guid AccountId = default);
