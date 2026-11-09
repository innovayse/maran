using Maran.Modules.Dns.Persistence;

namespace Maran.Modules.Dns.Services;

/// <summary>
/// Reads the names of zones that belong to OTHER accounts and collide with a candidate, which the tenant
/// filter hides from every customer by design.
/// </summary>
/// <remarks>
/// The bypass is deliberate and narrow, and it is the reason this is a type of its own rather than a line in
/// a handler. The question "does this name collide with somebody else's zone" is a question about the whole
/// server, and a customer's own context cannot answer it: the filter would hide exactly the rows that
/// matter, and the daemon answers from the most specific zone it holds, so a collision is a hijack. What
/// comes back is only the names that collide, never an owner, a record or a count, and the handler turns
/// them into one refusal.
/// </remarks>
public sealed class ZoneNameDirectory
{
    /// <summary>The module's context, whose tenant filter is bypassed for exactly the reads below.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>Creates the directory over this module's context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public ZoneNameDirectory(DnsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Finds the zones of other accounts whose name is <paramref name="candidate"/>, a parent of it, or a
    /// child of it (the last as a superset: the caller weighs the names with <c>ZoneOverlapPolicy</c>).
    /// </summary>
    /// <param name="accountId">The account creating the zone; its own zones may nest and are not returned.</param>
    /// <param name="candidate">The normalised apex being created.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The colliding names, empty when there is none.</returns>
    public async Task<IReadOnlyList<string>> CollidingNamesOfOtherAccountsAsync(
        Guid accountId,
        string candidate,
        CancellationToken cancellationToken)
    {
        var parents = ParentsOf(candidate);

        // A SUPERSET of the children: names that merely contain the suffix are returned too, and the overlap
        // policy, which compares with an exact ordinal suffix, decides. A plain Contains is translated by the
        // provider without a culture-sensitive string comparison.
        var childMarker = "." + candidate;

#pragma warning disable RS0030 // the filter hides other accounts' zones, which is exactly what this check must see
        return await _dbContext.Zones
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(zone => zone.AccountId != accountId
                && (zone.Name == candidate
                    || parents.Contains(zone.Name)
                    || zone.Name.Contains(childMarker)))
            .Select(zone => zone.Name)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
    }

    /// <summary>Lists every name above <paramref name="name"/>: <c>a.b.c</c> has parents <c>b.c</c> and <c>c</c>.</summary>
    /// <param name="name">A normalised name.</param>
    /// <returns>The suffixes that are names of their own.</returns>
    private static List<string> ParentsOf(string name)
    {
        var parents = new List<string>();
        var dot = name.IndexOf('.');
        while (dot >= 0)
        {
            name = name[(dot + 1)..];
            parents.Add(name);
            dot = name.IndexOf('.');
        }

        return parents;
    }
}
