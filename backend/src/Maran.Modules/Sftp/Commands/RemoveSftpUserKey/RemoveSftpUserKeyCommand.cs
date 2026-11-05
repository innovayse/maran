using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Sftp.Commands.RemoveSftpUserKey;

/// <summary>
/// Removes one public key from one of the caller's SFTP logins and ends that login's open sessions,
/// so a revoked key stops meaning access (issue #339).
/// </summary>
/// <remarks>
/// Both identifiers come from the route and the command is built from them: there is no body. The key
/// is looked up by BOTH ids, so a key id that belongs to another login — the caller's own or somebody
/// else's — answers "not found" exactly as a key that does not exist.
/// </remarks>
/// <param name="SftpUserId">Which login the key belongs to. A row identifier, never a system user name.</param>
/// <param name="KeyId">Which key to remove.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record RemoveSftpUserKeyCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid SftpUserId,
    [property: JsonIgnore][property: BindNever][BindNever] Guid KeyId,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
