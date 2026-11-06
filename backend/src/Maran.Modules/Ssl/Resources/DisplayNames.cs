namespace Maran.Modules.Ssl.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries every user-facing name the Ssl
/// module owns: <c>SslModuleDisplayName</c> (resolved via <see cref="SslManifest"/>'s
/// <c>DisplayNameKey</c>) and one <c>AcmeDirectoryKind&lt;Kind&gt;</c> entry per kind
/// <see cref="Domain.Policies.AcmeDirectoryKindPolicy"/> can answer.
/// </summary>
public sealed class DisplayNames
{
}
