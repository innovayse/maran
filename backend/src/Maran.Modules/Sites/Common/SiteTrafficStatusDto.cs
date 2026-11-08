namespace Maran.Modules.Sites.Common;

/// <summary>How many requests of the window were answered with one HTTP status code.</summary>
/// <param name="Code">The HTTP status code; a number, not a name, so it needs no localization.</param>
/// <param name="Requests">How many requests were answered with it.</param>
public sealed record SiteTrafficStatusDto(int Code, long Requests);
