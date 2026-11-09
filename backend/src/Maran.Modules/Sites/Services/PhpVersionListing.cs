using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Persistence;
using Maran.SharedKernel.Callers;
using AgentPhpVersionDto = Maran.Agent.Client.Services.PhpService.PhpVersionDto;
using PhpVersionDto = Maran.Modules.Sites.Common.PhpVersionDto;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Turns the agent's installed PHP versions into the panel's outward <see cref="PhpVersionDto"/>s,
/// each with where it stands in its support life and, for an administrator, how many sites use it.
/// </summary>
/// <remarks>
/// <para>
/// One type for both endpoints that answer with the installed list: the list itself and the install,
/// which answers the list after the attempt. Written twice, the support life and the site count would
/// be two places that can drift, and the install would answer a list the page does not recognise.
/// </para>
/// <para>
/// <b>The site count is the administrator's.</b> It is read through the request's own
/// tenant-filtered context, which shows an administrator every account's sites; a customer is not
/// even asked the question, because a count of sites on a version tells them how many other accounts
/// share the host's runtime. The support life comes from the panel's own table and the injected clock,
/// never from a feed.
/// </para>
/// </remarks>
public sealed class PhpVersionListing
{
    /// <summary>The module's context, tenant-filtered for the request's caller.</summary>
    private readonly SitesDbContext _context;

    /// <summary>The injected clock the support life is read against.</summary>
    private readonly IClock _clock;

    /// <summary>The caller, to decide whether the site count is theirs to see.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Resolves the localized support status words.</summary>
    private readonly SiteDisplayNames _displayNames;

    /// <summary>Creates the listing.</summary>
    /// <param name="context">The module's context, tenant-filtered for the request's caller.</param>
    /// <param name="clock">The injected clock the support life is read against.</param>
    /// <param name="currentUser">The caller, to decide whether the site count is theirs to see.</param>
    /// <param name="displayNames">Resolves the localized support status words.</param>
    public PhpVersionListing(
        SitesDbContext context,
        IClock clock,
        ICurrentUser currentUser,
        SiteDisplayNames displayNames)
    {
        _context = context;
        _clock = clock;
        _currentUser = currentUser;
        _displayNames = displayNames;
    }

    /// <summary>Describes the installed versions in the panel's outward shape, keeping the agent's order.</summary>
    /// <param name="versions">The versions the agent reported as installed.</param>
    /// <param name="cancellationToken">Cancels the site count.</param>
    /// <returns>One outward view per version, without the FPM socket directory.</returns>
    public async Task<IReadOnlyList<PhpVersionDto>> DescribeAsync(
        IReadOnlyList<AgentPhpVersionDto> versions,
        CancellationToken cancellationToken)
    {
        var counts = await CountSitesAsync(cancellationToken);
        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        return versions
            .Select(version =>
            {
                var reading = PhpLifecycle.Read(version.Version, today);
                var lifecycle = PhpLifecycleMapper.From(reading, _displayNames.Of(reading.Status));
                int? sitesUsing = counts is null ? null : counts.GetValueOrDefault(version.Version);

                return PhpVersionMapper.From(version, lifecycle, sitesUsing);
            })
            .ToList();
    }

    /// <summary>Counts the sites bound to each PHP version, for an administrator only.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The count per version, or <see langword="null"/> when the caller is not an administrator.</returns>
    private async Task<Dictionary<string, int>?> CountSitesAsync(CancellationToken cancellationToken)
    {
        if (_currentUser.Caller is not AdministratorCaller)
        {
            return null;
        }

        var rows = await _context.Sites
            .GroupBy(site => site.PhpVersion)
            .Select(group => new { Version = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            row =>
            {
                return row.Version;
            },
            row =>
            {
                return row.Count;
            },
            StringComparer.Ordinal);
    }
}
