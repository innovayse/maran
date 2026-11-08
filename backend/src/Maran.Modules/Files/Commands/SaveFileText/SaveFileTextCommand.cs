using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Files.Commands.SaveFileText;

/// <summary>Replaces the text of one file of an account's home with the editor's content.</summary>
/// <remarks>
/// A save keeps the file's permissions: the handler reads the existing mode and writes with it, so an
/// edit never changes who can read or run the file. A file that does not exist yet is created with
/// the default mode. Bound directly by <c>PUT /api/v1/files/content</c>; there is no separate request type
/// (rules/csharp.md "Server-established members of a command").
/// </remarks>
/// <param name="AccountId">
/// The account whose home it is, resolved in the handler. The resolution is the tenant boundary:
/// another tenant's account answers "not found".
/// </param>
/// <param name="Path">The file, relative to the home. Never empty.</param>
/// <param name="Content">The file's new content, as text; written as UTF-8.</param>
/// <param name="ExpectedModifiedAt">
/// The modification time the editor loaded the file with, or null to save without checking (a file the
/// editor is creating). When it is given and the file has changed since, the save is refused with
/// <c>FilesEditConflict</c>. The comparison is not atomic with the write: a change landing between the
/// two is overwritten, and that window is narrow but real.
/// </param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and stamped by
/// the action. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers and
/// stamped by the action. Never bound from the request.</param>
public sealed record SaveFileTextCommand(
    Guid AccountId,
    string Path,
    string Content,
    DateTimeOffset? ExpectedModifiedAt = null,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
