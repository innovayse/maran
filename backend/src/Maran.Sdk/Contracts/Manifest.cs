namespace Maran.Sdk.Contracts;

/// <summary>
/// Identity every module (ours and third-party) declares about itself, published through the
/// modules catalogue endpoint (<c>GET /api/v1/modules</c>) so the SPA and the licence system know
/// what is installed without inspecting assemblies (rules/csharp.md "Canonical backend layout").
/// </summary>
/// <param name="Id">Stable machine id; equals the module's <see cref="Interfaces.IPanelModule.Name"/> and its PostgreSQL schema name.</param>
/// <param name="DisplayNameKey">
/// The resx key resolved (via <see cref="SharedKernel.Interfaces.IErrorTextProvider"/>, in the
/// request culture) for this module's human-readable name. The module's own
/// <c>Resources/Messages*.resx</c> supplies the actual English/Russian/Armenian text — a module is
/// discovered at runtime, so the SPA can never own the label for one it was not built knowing
/// about, and the backend owns all user-facing text regardless (rules/csharp.md "The backend owns
/// all user-facing message text").
/// </param>
/// <param name="Version">Semantic version of this module build.</param>
/// <param name="Tier">The licence tier this module ships under.</param>
/// <param name="Dependencies">Ids of other modules this module requires to be loaded first.</param>
public sealed record Manifest(
    string Id,
    string DisplayNameKey,
    string Version,
    LicenceTier Tier,
    IReadOnlyList<string> Dependencies);
