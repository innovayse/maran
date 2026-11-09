using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Files.Commands.RenameFileEntry;

/// <summary>Renames one entry inside the folder it is in. It never moves an entry to another folder.</summary>
/// <remarks>Bound directly by <c>POST /api/v1/files/rename</c>.</remarks>
/// <param name="AccountId">
/// The account whose home it is, resolved in the handler. The resolution is the tenant boundary:
/// another tenant's account answers "not found".
/// </param>
/// <param name="Path">The entry to rename, relative to the home; never the home itself.</param>
/// <param name="NewName">The new name: exactly one path segment, no separator.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers
/// and stamped by the action. Never bound from the request.</param>
public sealed record RenameFileEntryCommand(
    Guid AccountId,
    string Path,
    string NewName,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
