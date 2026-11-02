using System.Text.Json.Serialization;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.ChangeSubUserGrants;

/// <summary>Replaces the features one of the caller's sub-users may use.</summary>
/// <param name="Features">
/// The complete new set of single features; an empty list takes every gated feature away.
/// </param>
/// <param name="UserId">
/// The sub-user's login id, taken from the route rather than the body (rules/csharp.md "An endpoint
/// binds its command directly"), so the URL and the body cannot name different people. Defaults to
/// empty so the body need not carry it; the action always stamps it.
/// </param>
/// <param name="IpAddress">The owner's address, established by the server and stamped by the action.
/// Never bound from a request body.</param>
/// <param name="UserAgent">The owner's user agent, established by the server and stamped by the action.
/// Never bound from a request body.</param>
public sealed record ChangeSubUserGrantsCommand(
    IReadOnlyList<AccountFeature> Features,
    [property: JsonIgnore][property: BindNever][BindNever] Guid UserId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
