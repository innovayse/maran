using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Files.Commands.CopyFileEntries;

/// <summary>Copies each named entry into one folder, keeping its name. A batch: every item is tried in order.</summary>
/// <remarks>
/// A failure of one item does not stop the others and is reported against that item. Nothing is ever
/// replaced: an item whose name is taken in the destination folder fails with a conflict. Bound directly by
/// <c>POST /api/v1/files/copy</c>.
/// </remarks>
/// <param name="AccountId">
/// The account whose home it is, resolved in the handler. The resolution is the tenant boundary:
/// another tenant's account answers "not found".
/// </param>
/// <param name="SourcePaths">The entries, relative to the home. At least one, at most the configured batch size.</param>
/// <param name="DestinationDirectory">The folder they land in, relative to the home; empty for the home itself.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers
/// and stamped by the action. Never bound from the request.</param>
public sealed record CopyFileEntriesCommand(
    Guid AccountId,
    IReadOnlyList<string> SourcePaths,
    string DestinationDirectory,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
