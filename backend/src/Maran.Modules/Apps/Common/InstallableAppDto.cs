using Maran.Modules.Apps.Domain.Enums;

namespace Maran.Modules.Apps.Common;

/// <summary>One app the host's package sources can install, for the picker of the app store and the first-run setup.</summary>
/// <param name="AppId">The catalogue id.</param>
/// <param name="Name">The app's name for the request's language.</param>
/// <param name="Kind">What the app is for.</param>
/// <param name="KindName">The kind in the request's language.</param>
/// <param name="Installed">Whether the app is already installed, so the picker can show it as taken.</param>
public sealed record InstallableAppDto(
    string AppId,
    string Name,
    AppKind Kind,
    string KindName,
    bool Installed);
