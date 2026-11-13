using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>Builds the <see cref="SiteDirectory"/> a test reads sites through.</summary>
public static class SiteDirectories
{
    /// <summary>A directory whose accounts all exist and sit on the default plan unless a plan is given.</summary>
    /// <param name="context">The context the sites are read from.</param>
    /// <param name="limits">The PHP bounds every account has; the default plan's when omitted.</param>
    /// <returns>The directory.</returns>
    public static SiteDirectory Over(SitesDbContext context, PhpLimitsSnapshot? limits = null)
    {
        var directory = new StubAccountDirectory
        {
            Fallback = id =>
            {
                return Snapshots.Account(id, "acme") with { PhpLimits = limits ?? PhpLimitsSnapshot.Default };
            },
        };

        return new SiteDirectory(context, new SitePlanLimits(directory));
    }
}
