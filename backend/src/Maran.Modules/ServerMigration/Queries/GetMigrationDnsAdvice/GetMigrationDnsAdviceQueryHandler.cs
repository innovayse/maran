using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Interfaces;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;

namespace Maran.Modules.ServerMigration.Queries.GetMigrationDnsAdvice;

/// <summary>
/// Handles <see cref="GetMigrationDnsAdviceQuery"/>: the DNS helper (issue #197, phase 2, decision D19).
/// </summary>
/// <remarks>
/// <b>Only the domains in the job are looked up</b> (a primary domain and the sites of the snapshot), with the system
/// resolver, and the answer is text: addresses and whether one of them is an address of this server. The helper never
/// connects to what it resolved. It reads A and AAAA records only; a record's TTL and the name servers are not
/// available from the system resolver, so the screen gives the advice to lower the TTL before the cut-over as a
/// written instruction instead of a measurement. At most <see cref="MaxDomains"/> domains are looked up per call.
/// </remarks>
public sealed class GetMigrationDnsAdviceQueryHandler
{
    /// <summary>The most domains one call looks up.</summary>
    public const int MaxDomains = 500;

    /// <summary>This module's database.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The system resolver.</summary>
    private readonly IDomainResolver _resolver;

    /// <summary>The addresses this server holds.</summary>
    private readonly ILocalAddressSource _local;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">This module's database.</param>
    /// <param name="resolver">The system resolver.</param>
    /// <param name="local">The addresses this server holds.</param>
    public GetMigrationDnsAdviceQueryHandler(ServerMigrationDbContext dbContext, IDomainResolver resolver, ILocalAddressSource local)
    {
        _dbContext = dbContext;
        _resolver = resolver;
        _local = local;
    }

    /// <summary>Resolves the job's domains.</summary>
    /// <param name="query">The request.</param>
    /// <param name="cancellationToken">Cancels the lookups.</param>
    /// <returns>The advice, or <c>MigrationJobNotFound</c>.</returns>
    public async Task<Result<MigrationDnsAdviceDto>> HandleAsync(GetMigrationDnsAdviceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await _dbContext.MigrationJobs.AsNoTracking().AnyAsync(job => job.Id == query.JobId, cancellationToken))
        {
            return Result<MigrationDnsAdviceDto>.Fail(Error.Of(nameof(ErrorMessages.MigrationJobNotFound), ErrorType.NotFound));
        }

        var accounts = await _dbContext.MigrationJobAccounts.AsNoTracking()
            .Where(row => row.JobId == query.JobId)
            .ToListAsync(cancellationToken);
        var domains = accounts
            .SelectMany(account =>
            {
                return MigrationSectionMapper.ReadInventory(account).Sites.Select(site =>
            {
                return site.Domain;
            })
                .Concat(account.PrimaryDomain is null ? [] : [account.PrimaryDomain]);
            })
            .Select(domain =>
            {
                return domain.ToLowerInvariant();
            })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Take(MaxDomains)
            .ToList();
        var own = _local.Read();
        var rows = new List<MigrationDnsRowDto>();

        foreach (var domain in domains)
        {
            var addresses = await _resolver.ResolveAsync(domain, cancellationToken);
            rows.Add(new MigrationDnsRowDto(
                domain,
                addresses.Select(address =>
                {
                    return address.ToString();
                }).ToList(),
                addresses.Any(own.Contains),
                addresses.Count > 0));
        }

        return Result<MigrationDnsAdviceDto>.Ok(
            new MigrationDnsAdviceDto(rows, own.Select(address =>
            {
                return address.ToString();
            }).Order(StringComparer.Ordinal).ToList()));
    }
}
