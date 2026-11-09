using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Files.Commands.ExtractFileArchive;

/// <summary>Unpacks one archive into a folder of the account's home.</summary>
/// <remarks>
/// The format is read from the file name by <c>ArchiveFormatPolicy</c> and the agent verifies the real
/// format. The panel does not enforce the extraction bounds; it states them and the agent holds them.
/// Bound directly by <c>POST /api/v1/files/extract</c>.
/// </remarks>
/// <param name="AccountId">
/// The account whose home it is, resolved in the handler. The resolution is the tenant boundary:
/// another tenant's account answers "not found".
/// </param>
/// <param name="ArchivePath">The archive to unpack, relative to the home.</param>
/// <param name="DestinationDirectory">The folder to unpack into, relative to the home; created when absent.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers
/// and stamped by the action. Never bound from the request.</param>
public sealed record ExtractFileArchiveCommand(
    Guid AccountId,
    string ArchivePath,
    string DestinationDirectory,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
