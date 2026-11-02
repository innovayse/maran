using Maran.Modules.Monitoring.Domain.Enums;

namespace Maran.Modules.Monitoring.Common;

/// <summary>One server the panel manages, as the shell's server selector shows it (issue #319).</summary>
/// <remarks>
/// The kind crosses as an enum member and its name beside it, resolved by the backend in the request's
/// culture, because the SPA holds no text for a server-side fact (rules/architecture.md).
/// </remarks>
/// <param name="Kind">How the panel reaches the server.</param>
/// <param name="KindDisplayName">What that kind is called, in the request's culture.</param>
/// <param name="HostName">
/// The host name the panel is configured to be reached by; <see langword="null"/> when none is
/// configured, in which case the screen shows the kind alone rather than a made-up name.
/// </param>
public sealed record ServerDto(ServerKind Kind, string KindDisplayName, string? HostName);
