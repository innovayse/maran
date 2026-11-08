namespace Maran.Modules.Sites.Domain.Enums;

/// <summary>Which of a site's apex / "www." pair of names is canonical.</summary>
/// <remarks>
/// Choosing one makes the other redirect permanently to it, so a visitor and a search engine see one
/// address. <see cref="None"/> is a real choice — "answer on every name the site claims" — and not an
/// absent value.
/// </remarks>
public enum CanonicalHost
{
    /// <summary>No canonical redirect: the site answers on every name it claims.</summary>
    None = 0,

    /// <summary>The "www." name is canonical; the apex redirects to it.</summary>
    Www = 1,

    /// <summary>The apex name is canonical; the "www." name redirects to it.</summary>
    Apex = 2,
}
