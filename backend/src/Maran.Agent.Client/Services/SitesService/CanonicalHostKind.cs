namespace Maran.Agent.Client.Services.SitesService;

/// <summary>Which of a site's apex / "www." pair of names is canonical, as the panel names it.</summary>
/// <remarks>
/// A panel-side mirror of the wire's <c>CanonicalHost</c>, so callers outside this project never hold
/// a generated protobuf type. <see cref="None"/> is a real answer ("no canonical redirect"), not an
/// unspecified wire value: the wire's zero maps from it.
/// </remarks>
public enum CanonicalHostKind
{
    /// <summary>No canonical redirect: the site answers on every name it claims.</summary>
    None = 0,

    /// <summary>The "www." name is canonical; the apex redirects to it.</summary>
    Www = 1,

    /// <summary>The apex name is canonical; the "www." name redirects to it.</summary>
    Apex = 2,
}
