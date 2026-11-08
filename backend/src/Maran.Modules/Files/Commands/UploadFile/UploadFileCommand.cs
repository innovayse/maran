using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Files.Commands.UploadFile;

/// <summary>Writes the request body, as it arrives, to one file of an account's home.</summary>
/// <remarks>
/// <para>
/// The body is raw bytes in the request and never a multipart form: a form is spooled to disk by the
/// panel process, which runs unprivileged and would hold a customer's bytes outside their home. The
/// handler forwards the stream to the agent as it is read and counts the bytes itself, so memory use
/// does not depend on the file's size.
/// </para>
/// <para>
/// <b>This command carries a live stream and is meaningful on the local bus only.</b> A <see cref="Stream"/>
/// cannot be written into a durable or external queue's envelope, and <c>[JsonIgnore]</c> would drop
/// it silently, so dispatching this command through one would deliver a command with no file. The
/// upload action invokes it in process, which is the only way it is ever sent.
/// </para>
/// </remarks>
/// <param name="AccountId">
/// The account whose home it is, resolved in the handler. The resolution is the tenant boundary:
/// another tenant's account answers "not found".
/// </param>
/// <param name="Path">The destination file, relative to the home. Never empty.</param>
/// <param name="Overwrite">
/// Whether an existing file at the path may be replaced. False refuses with <c>FilesEntryExists</c>; the
/// check is not atomic with the write, so a file created in between is replaced.
/// </param>
/// <param name="Body">The request body, established by the server from the request and stamped by the
/// action. Never bound from the request.</param>
/// <param name="DeclaredBytes">The length the request declared, or null when it declared none; established by
/// the server and stamped by the action. A hint that refuses an obviously oversized upload before anything
/// is opened and never a measurement: the bytes are counted as they are read.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and stamped by
/// the action. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers and
/// stamped by the action. Never bound from the request.</param>
public sealed record UploadFileCommand(
    Guid AccountId,
    string Path,
    bool Overwrite = false,
    [property: JsonIgnore][property: BindNever][BindNever] Stream? Body = null,
    [property: JsonIgnore][property: BindNever][BindNever] long? DeclaredBytes = null,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
