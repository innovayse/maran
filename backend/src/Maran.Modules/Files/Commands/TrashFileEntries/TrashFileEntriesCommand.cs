using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Files.Commands.TrashFileEntries;

/// <summary>Moves each named entry into the account's trash, from which it can be restored until the retention ends.</summary>
/// <remarks>A batch: every item is tried in order. Bound directly by <c>POST /api/v1/files/trash</c>.</remarks>
/// <param name="AccountId">
/// The account whose home it is, resolved in the handler. The resolution is the tenant boundary:
/// another tenant's account answers "not found".
/// </param>
/// <param name="Paths">The entries, relative to the home. At least one, at most the configured batch size.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers
/// and stamped by the action. Never bound from the request.</param>
public sealed record TrashFileEntriesCommand(
    Guid AccountId,
    IReadOnlyList<string> Paths,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
