using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Files.Commands.RestoreTrashEntries;

/// <summary>Moves each named trash entry back to where it was, without replacing anything.</summary>
/// <remarks>
/// A batch: every item is tried in order. An entry whose old place is taken again fails with a conflict and
/// stays in the trash. Bound directly by <c>POST /api/v1/files/trash/restore</c>.
/// </remarks>
/// <param name="AccountId">
/// The account whose home it is, resolved in the handler. The resolution is the tenant boundary:
/// another tenant's account answers "not found".
/// </param>
/// <param name="TrashIds">The ids the trash listing reported. At least one, at most the configured batch size.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers
/// and stamped by the action. Never bound from the request.</param>
public sealed record RestoreTrashEntriesCommand(
    Guid AccountId,
    IReadOnlyList<string> TrashIds,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
