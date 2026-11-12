using Maran.Modules.Apps.Domain.Enums;

namespace Maran.Modules.Apps.Common;

/// <summary>The apps the host can install, and why no Node.js app is among them when none is (issue #532).</summary>
/// <param name="Apps">The installable apps in catalogue order, installed ones included.</param>
/// <param name="NodejsUnavailable">
/// Why the Node.js apps that are not installed are missing from <paramref name="Apps"/>, or <see langword="null"/>
/// when they are not missing for a reason. The app store says it instead of silently omitting Node.js.
/// </param>
/// <param name="NodejsUnavailableName">
/// <paramref name="NodejsUnavailable"/> as a sentence an operator reads, in the request's language, or
/// <see langword="null"/> when there is no reason.
/// </param>
public sealed record InstallableAppsDto(
    IReadOnlyList<InstallableAppDto> Apps,
    NodejsUnavailableReason? NodejsUnavailable,
    string? NodejsUnavailableName);
