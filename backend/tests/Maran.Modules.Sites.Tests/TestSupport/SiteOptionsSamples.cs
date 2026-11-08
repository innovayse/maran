using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.ValueObjects;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>Ready-made option sets, so each test states only the variation it is about.</summary>
public static class SiteOptionsSamples
{
    /// <summary>A bcrypt-shaped hash a test can store without paying for a real one.</summary>
    public const string StoredHash = "$2y$12$abcdefghijklmnopqrstuuabcdefghijklmnopqrstuvwxyz01234";

    /// <summary>A set with every group filled in, for tests that must see each one carried.</summary>
    /// <returns>Canonical www, 64 MiB, 30 days, one redirect, one user, one allow-list address and a limit of 10 requests a second with a burst of 20.</returns>
    public static SiteOptionsValues Full()
    {
        return new SiteOptionsValues(
            CanonicalHost.Www,
            64,
            30,
            [new SiteRedirectValue("/old", RedirectMatch.Exact, "/new", RedirectStatus.Permanent)],
            [new SiteBasicAuthUserValue("alice", StoredHash)],
            IpAccessMode.AllowList,
            ["203.0.113.0/24"],
            new SiteRequestRate(10, 20));
    }

    /// <summary>A set with nothing in it.</summary>
    /// <returns>The empty set.</returns>
    public static SiteOptionsValues Empty()
    {
        return new SiteOptionsValues(CanonicalHost.None, null, null, [], [], null, [], null);
    }
}
