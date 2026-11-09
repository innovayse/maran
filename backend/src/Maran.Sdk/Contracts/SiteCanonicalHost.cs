namespace Maran.Sdk.Contracts;

/// <summary>Which of a site's apex / "www." pair of names is canonical, as one module reports it to another.</summary>
/// <remarks>
/// A deliberate second declaration of a closed set the Sites module owns, for the reason
/// <see cref="SiteBackend"/> gives. <see cref="None"/> is a real answer ("no canonical redirect").
/// </remarks>
public enum SiteCanonicalHost
{
    /// <summary>No canonical redirect: the site answers on every name it claims.</summary>
    None = 0,

    /// <summary>The "www." name is canonical; the apex redirects to it.</summary>
    Www = 1,

    /// <summary>The apex name is canonical; the "www." name redirects to it.</summary>
    Apex = 2,
}
