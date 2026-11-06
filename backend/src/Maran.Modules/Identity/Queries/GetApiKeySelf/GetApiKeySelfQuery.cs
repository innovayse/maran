using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Queries.GetApiKeySelf;

/// <summary>Reads what the calling API key may learn about itself.</summary>
/// <param name="ApiKeyId">
/// The calling key, established by the server from the authenticated principal and stamped by the
/// action. Never bound from the request, so there is no parameter with which to ask about another key
/// (rules/csharp.md "Server-established members of a command").
/// </param>
public sealed record GetApiKeySelfQuery(
    [property: JsonIgnore][property: BindNever][BindNever] Guid ApiKeyId);
