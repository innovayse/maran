using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Files.Commands.DeleteFileEntries;

/// <summary>Removes each named entry for good, folders with everything inside them, bypassing the trash.</summary>
/// <remarks>
/// Not undoable, so it must be asked for deliberately: <paramref name="Confirm"/> has to be true. A batch:
/// every item is tried in order. Bound directly by <c>POST /api/v1/files/delete</c>.
/// </remarks>
/// <param name="AccountId">
/// The account whose home it is, resolved in the handler. The resolution is the tenant boundary:
/// another tenant's account answers "not found".
/// </param>
/// <param name="Paths">The entries, relative to the home. At least one, at most the configured batch size.</param>
/// <param name="Confirm">Must be true; a permanent delete that was not confirmed is refused.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers
/// and stamped by the action. Never bound from the request.</param>
public sealed record DeleteFileEntriesCommand(
    Guid AccountId,
    IReadOnlyList<string> Paths,
    bool Confirm,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
