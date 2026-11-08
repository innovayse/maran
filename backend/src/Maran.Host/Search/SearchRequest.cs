namespace Maran.Host.Search;

/// <summary>
/// The body of <c>POST /api/v1/search</c>. The term travels in the body, never in the URL, so it cannot
/// reach an access log, a browser history entry or a <c>Referer</c> (docs/superpowers/notes/
/// 2026-11-07-global-search-threat-note.md).
/// </summary>
/// <param name="Term">
/// The text typed into the palette; validated by the endpoint, not here. A body that leaves it out binds
/// to null and is refused by that validation with a code, like a term that is too short.
/// </param>
public sealed record SearchRequest(string Term);
