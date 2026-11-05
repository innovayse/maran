using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Sftp.Commands.AddSftpUserKey;

/// <summary>
/// Registers a public key for one of the caller's SFTP logins, so the login can be signed into with
/// the matching private key.
/// </summary>
/// <remarks>
/// The command carries the key as the customer pasted it and nothing the server establishes from the
/// request: which login it is for comes from the route, and the fingerprint and algorithm are
/// computed by the panel from the key it validated, never read from the request. The key travels the
/// local bus only; it is never written to a durable queue envelope.
/// </remarks>
/// <param name="Label">The customer's name for the key; trimmed, 1 to 64 characters, no control character.</param>
/// <param name="PublicKey">
/// One OpenSSH public-key line, <c>&lt;algorithm&gt; &lt;base64&gt; [comment]</c>. Options and the
/// comment are not kept; a line with a leading option is refused.
/// </param>
/// <param name="SftpUserId">
/// Which login the key is for. Established by the server from the route and stamped by the action;
/// never bound from the request, where a different id in the body could otherwise win over the URL.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record AddSftpUserKeyCommand(
    string Label,
    string PublicKey,
    [property: JsonIgnore][property: BindNever][BindNever] Guid SftpUserId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
