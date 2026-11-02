using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Queries.GetViewAs;

/// <summary>Asks whether the caller's token is a view-as token, and of what (issues #317, #314).</summary>
/// <param name="SessionId">The caller's session, from their access token's <c>sid</c>, stamped by the
/// action. Never bound — a caller can ask only about the token they are holding.</param>
/// <param name="ViewedUserId">The login the caller's token speaks for, stamped by the action. Never bound.</param>
public sealed record GetViewAsQuery(
    [property: JsonIgnore][property: BindNever][BindNever] Guid SessionId,
    [property: JsonIgnore][property: BindNever][BindNever] Guid ViewedUserId);
