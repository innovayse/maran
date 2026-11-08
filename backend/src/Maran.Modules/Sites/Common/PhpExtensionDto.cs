using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Common;

/// <summary>Outward view of one catalogue PHP extension of one installed version.</summary>
/// <param name="Name">The catalogue name, lowercase, e.g. <c>gd</c>; never a package name.</param>
/// <param name="Label">The extension as the reader's language names it, e.g. "Images (GD)"; shown as received.</param>
/// <param name="Enabled">Whether the version's php-fpm has the extension loaded.</param>
/// <param name="Availability">
/// What this host's OS family can do with the extension, for the administrator who can switch it;
/// <see langword="null"/> for a customer, who can switch nothing and has no use for how the host's
/// packages are arranged.
/// </param>
/// <param name="AvailabilityName">
/// <paramref name="Availability"/> as the reader's language words it, e.g. "Can be switched on, not
/// off, on this server"; shown as received. <see langword="null"/> exactly when
/// <paramref name="Availability"/> is.
/// </param>
public sealed record PhpExtensionDto(
    string Name,
    string Label,
    bool Enabled,
    ExtensionAvailability? Availability,
    string? AvailabilityName);
