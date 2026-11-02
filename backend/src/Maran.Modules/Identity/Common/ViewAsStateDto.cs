namespace Maran.Modules.Identity.Common;

/// <summary>
/// The answer to "is the token making this request a view-as token, and if so of what"
/// (<c>GET /api/v1/view-as</c>).
/// </summary>
/// <param name="Viewing">
/// The view-as session the token belongs to, or null when the caller is signed in as themselves. Null
/// is the ordinary answer, not a failure: every page load of an operator's own session asks it.
/// </param>
public sealed record ViewAsStateDto(ViewingAsDto? Viewing);
