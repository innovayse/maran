using System.Text.Json.Serialization;
using Maran.Modules.Files.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Files.Commands.CreateFileArchive;

/// <summary>Packs the named entries into one archive file inside the account's home.</summary>
/// <remarks>
/// A folder is downloaded by archiving it into the current folder and downloading the archive. The sizes
/// the archive may reach are the agent's bounds, taken from the module's options. Bound directly by
/// <c>POST /api/v1/files/archive</c>.
/// </remarks>
/// <param name="AccountId">
/// The account whose home it is, resolved in the handler. The resolution is the tenant boundary:
/// another tenant's account answers "not found".
/// </param>
/// <param name="SourcePaths">The entries to pack, relative to the home. At least one, at most the configured batch size.</param>
/// <param name="DestinationPath">The archive file to write, relative to the home; its name ends in the format's suffix.</param>
/// <param name="Format">The container format; a closed set.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers
/// and stamped by the action. Never bound from the request.</param>
public sealed record CreateFileArchiveCommand(
    Guid AccountId,
    IReadOnlyList<string> SourcePaths,
    string DestinationPath,
    ArchiveFormatKind Format,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
