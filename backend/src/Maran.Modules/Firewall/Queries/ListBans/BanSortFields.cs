namespace Maran.Modules.Firewall.Queries.ListBans;

/// <summary>
/// The public names the bans list sorts by — the <c>sort</c> parameter's vocabulary, written once and
/// read by <see cref="ListBansSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class BanSortFields
{
    /// <summary>The banned address, as the list shows it.</summary>
    public const string Address = "address";

    /// <summary>When the ban was placed.</summary>
    public const string BannedAt = "bannedAt";

    /// <summary>When the ban runs out; a ban that lasts until somebody lifts it has no such instant.</summary>
    public const string ExpiresAt = "expiresAt";
}
