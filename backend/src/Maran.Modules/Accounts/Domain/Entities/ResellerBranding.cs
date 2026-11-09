namespace Maran.Modules.Accounts.Domain.Entities;

/// <summary>
/// What a reseller chose its customers' shell should show instead of the panel's own mark: a name, a
/// logo and an accent colour. One row per reseller that has set any of them; a reseller with no row, or a
/// row whose values are all empty, shows the panel's defaults.
/// </summary>
/// <remarks>
/// A row of its own and not columns on <see cref="Reseller"/>, because the two have different readers:
/// the reseller row is hidden from a customer, and the branding is the one thing of the reseller's a
/// customer is meant to read. The query filter on this entity says so (see <c>AccountsDbContext</c>).
/// </remarks>
public sealed class ResellerBranding
{
    /// <summary>The longest display name, in characters.</summary>
    public const int DisplayNameMaxLength = 40;

    /// <summary>
    /// The longest stored logo, in characters of its data URI. About 100 KB of image once decoded,
    /// which is far more than a header mark needs and small enough to travel inside every customer's
    /// branding read.
    /// </summary>
    public const int LogoMaxLength = 140_000;

    /// <summary>The reseller the branding belongs to; also the row's key, because a reseller has at most one.</summary>
    public Guid ResellerId { get; private set; }

    /// <summary>The name shown in place of the panel's, or <c>null</c> for the panel's own.</summary>
    public string? DisplayName { get; private set; }

    /// <summary>The accent colour as lower-case <c>#rrggbb</c>, or <c>null</c> for the panel's own.</summary>
    public string? AccentColor { get; private set; }

    /// <summary>The logo as a base64 data URI of a PNG, JPEG or WebP image, or <c>null</c> for the panel's own mark.</summary>
    public string? Logo { get; private set; }

    /// <summary>The instant the branding was last replaced.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates a reseller's branding.</summary>
    /// <param name="resellerId">The reseller the branding belongs to.</param>
    /// <param name="displayName">The display name, already validated, or <c>null</c>.</param>
    /// <param name="accentColor">The accent colour, already validated, or <c>null</c>.</param>
    /// <param name="logo">The logo data URI, already validated, or <c>null</c>.</param>
    /// <param name="updatedAt">The instant of the change, taken from <see cref="IClock"/>.</param>
    public ResellerBranding(Guid resellerId, string? displayName, string? accentColor, string? logo, DateTimeOffset updatedAt)
    {
        ResellerId = resellerId;
        Replace(displayName, accentColor, logo, updatedAt);
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private ResellerBranding()
    {
    }

    /// <summary>
    /// Replaces all three values at once; a value left <c>null</c> goes back to the panel's own. The
    /// colour is stored in lower case so two spellings of one colour are one value.
    /// </summary>
    /// <param name="displayName">The display name, already validated, or <c>null</c>.</param>
    /// <param name="accentColor">The accent colour, already validated, or <c>null</c>.</param>
    /// <param name="logo">The logo data URI, already validated, or <c>null</c>.</param>
    /// <param name="updatedAt">The instant of the change.</param>
    public void Replace(string? displayName, string? accentColor, string? logo, DateTimeOffset updatedAt)
    {
        DisplayName = displayName;
        AccentColor = accentColor?.ToLowerInvariant();
        Logo = logo;
        UpdatedAt = updatedAt;
    }
}
