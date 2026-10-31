namespace Maran.Modules.Identity.Queries.ListSessions;

/// <summary>
/// The public names the sessions list sorts by — the <c>sort</c> parameter's vocabulary, written once
/// and read by <see cref="ListSessionsSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class SessionSortFields
{
    /// <summary>
    /// When the session row was issued — the device's sign-in, or its latest token refresh, since a
    /// refresh rotates the chain onto a new row and revokes the old one. The newest is the device
    /// seen most recently.
    /// </summary>
    public const string IssuedAt = "issuedAt";

    /// <summary>When the device will be signed out unless it refreshes.</summary>
    public const string ExpiresAt = "expiresAt";
}
