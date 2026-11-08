using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Files.Commands.ChangeFilePermissions;

/// <summary>Changes the permission bits of one entry, and of everything inside it when it is a folder and asked to.</summary>
/// <remarks>
/// Plain permission bits only: setuid, setgid and the sticky bit cannot be written as three octal digits and
/// are refused again by the agent. Bound directly by <c>POST /api/v1/files/permissions</c>.
/// </remarks>
/// <param name="AccountId">
/// The account whose home it is, resolved in the handler. The resolution is the tenant boundary:
/// another tenant's account answers "not found".
/// </param>
/// <param name="Path">The entry to change, relative to the home; never the home itself.</param>
/// <param name="Mode">The new bits as three octal digits, 000 to 777.</param>
/// <param name="Recursive">Whether the change reaches everything inside a folder too.</param>
/// <param name="DirectoryMode">
/// The bits folders get in a recursive change, as three octal digits, 001 to 777. Absent, folders get
/// <paramref name="Mode"/> with execute added wherever read is set (644 gives 755), so that a recursive
/// change of files does not make folders untraversable. Zero is not a valid value: the agent reads zero as
/// "the same as the mode". Not read when <paramref name="Recursive"/> is false.
/// </param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers
/// and stamped by the action. Never bound from the request.</param>
public sealed record ChangeFilePermissionsCommand(
    Guid AccountId,
    string Path,
    string Mode,
    bool Recursive,
    string? DirectoryMode = null,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
