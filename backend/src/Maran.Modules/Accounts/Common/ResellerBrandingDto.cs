namespace Maran.Modules.Accounts.Common;

/// <summary>
/// What a reseller chose its customers' shell should show. Every member is <c>null</c> when it is not
/// set, which tells the shell to draw the panel's own: the shell holds no default of its own to fall
/// back on.
/// </summary>
/// <param name="DisplayName">The name shown in place of the panel's, or <c>null</c>.</param>
/// <param name="AccentColor">The accent colour as lower-case <c>#rrggbb</c> that carries white text, or <c>null</c>.</param>
/// <param name="Logo">The logo as a base64 data URI of a PNG, JPEG or WebP image, or <c>null</c>.</param>
public sealed record ResellerBrandingDto(string? DisplayName, string? AccentColor, string? Logo);
