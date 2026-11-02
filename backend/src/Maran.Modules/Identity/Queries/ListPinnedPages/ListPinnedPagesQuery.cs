namespace Maran.Modules.Identity.Queries.ListPinnedPages;

/// <summary>Reads the signed-in person's own pinned pages.</summary>
/// <param name="UserId">
/// Whose pins to read. Taken from the caller's own token by the controller and never from the
/// request, so there is no parameter with which to ask for somebody else's.
/// </param>
public sealed record ListPinnedPagesQuery(Guid UserId);
