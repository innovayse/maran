namespace Maran.Sdk.Contracts;

/// <summary>
/// One thing a search found: enough to draw a row and to open the thing, and nothing more.
/// </summary>
/// <remarks>
/// <b>An allow-list, never a copy of an entity.</b> <see cref="Title"/> and <see cref="Subtitle"/> carry
/// only fields the caller's role already reads on the object's own page; a hit never carries a hash, a
/// token, a key, a path or a status text. The route is not here: the SPA maps <see cref="Kind"/> and
/// <see cref="Id"/> to one of its own routes.
/// </remarks>
/// <param name="Kind">What was found.</param>
/// <param name="Id">The identity of the thing, as the screen that opens it addresses it.</param>
/// <param name="Title">The main line, usually the thing's name.</param>
/// <param name="Subtitle">A second line that says which field matched or what the thing is; may be empty.</param>
public sealed record SearchHit(SearchKind Kind, Guid Id, string Title, string Subtitle);
