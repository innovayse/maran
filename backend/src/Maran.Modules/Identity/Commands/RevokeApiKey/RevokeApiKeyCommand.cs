using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.RevokeApiKey;

/// <summary>Revokes one API key, so it authenticates nothing from the next request on.</summary>
/// <param name="ApiKeyId">The key to revoke, established by the server from the route and stamped by the action.
/// Never bound from the request body, so a body id can never disagree with the URL.</param>
/// <param name="CallerUserId">The login revoking it (the administrator, or a reseller's own login), established by the server
/// from their own access token and stamped by the action. Never bound from the request (rules/csharp.md "Server-established members of a command").</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and stamped by the action. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers and stamped by the action. Never bound from the request.</param>
/// <param name="OwnerResellerId">The reseller whose keys the caller may revoke, established by the server from the reseller's own
/// access token and stamped by the reseller's action; <c>null</c> on the administrator's, which revokes keys the administrator owns.
/// A key with another owner is answered exactly like a key that does not exist.</param>
public sealed record RevokeApiKeyCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid ApiKeyId,
    [property: JsonIgnore][property: BindNever][BindNever] Guid CallerUserId,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "",
    [property: JsonIgnore][property: BindNever][BindNever] Guid? OwnerResellerId = null);
