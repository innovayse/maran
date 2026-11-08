using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Accounts.Commands.SetResellerBranding;

/// <summary>
/// Replaces what the calling reseller's customers' shell shows. All three values are replaced together:
/// one left <c>null</c> goes back to the panel's own, and a command with all three <c>null</c> removes the
/// branding.
/// </summary>
/// <remarks>
/// Bound directly by <c>PUT /api/v1/reseller/branding</c>. It names no reseller: the reseller is the one
/// the caller's token names, so there is no id here to aim at another.
/// </remarks>
/// <param name="DisplayName">The name to show in place of the panel's; blank or <c>null</c> for the panel's own.</param>
/// <param name="AccentColor">The accent colour as <c>#rrggbb</c> with white-text contrast, or <c>null</c> for the panel's own.</param>
/// <param name="Logo">The logo as a base64 data URI of a PNG, JPEG or WebP image, or <c>null</c> for the panel's own mark.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Never bound from the request.</param>
public sealed record SetResellerBrandingCommand(
    string? DisplayName,
    string? AccentColor,
    string? Logo,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
