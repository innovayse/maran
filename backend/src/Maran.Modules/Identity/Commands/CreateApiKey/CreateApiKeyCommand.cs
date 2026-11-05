using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.CreateApiKey;

/// <summary>
/// Creates an API key: a long-lived credential an integration presents in the <c>X-Api-Key</c>
/// header, limited to the scopes, the address ranges and the lifetime named here.
/// </summary>
/// <param name="Name">The label shown in the key list, 1 to 64 characters with no control characters.</param>
/// <param name="Scopes">The scopes to grant, each from the list <c>GET api/v1/api-keys/scopes</c> offers.</param>
/// <param name="AllowedCidrs">
/// Address ranges the key may be used from (<c>203.0.113.0/24</c>, or a bare address), at most 16, or
/// <c>null</c> for a key not bound to any address. An empty list is refused as malformed. A range that matches every address (<c>/0</c>)
/// is refused: it binds nothing.
/// </param>
/// <param name="ExpiresAt">When the key stops working, which must be in the future, or <c>null</c> for a key that lives until revoked.</param>
/// <param name="CreatedByUserId">The administrator creating the key, established by the server from their own access token and
/// stamped by the action. Never bound from the request (rules/csharp.md "Server-established members of a command").</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and stamped by the action. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers and stamped by the action. Never bound from the request.</param>
public sealed record CreateApiKeyCommand(
    string Name,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string>? AllowedCidrs = null,
    DateTimeOffset? ExpiresAt = null,
    [property: JsonIgnore][property: BindNever][BindNever] Guid CreatedByUserId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
