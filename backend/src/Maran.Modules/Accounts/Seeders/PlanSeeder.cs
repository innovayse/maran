using Maran.Modules.Accounts.Domain;
using Maran.Modules.Accounts.Persistence;

namespace Maran.Modules.Accounts.Seeders;

/// <summary>
/// Seeds the standard plans a fresh installation ships with, so there is something to create an
/// account against before an operator has defined any plan of their own. Three tiers — a starter, a
/// business, and a top tier with a generous-but-finite ceiling rather than a literal "unlimited"
/// (this pass keeps every limit a plain, comparable integer; an explicit unlimited flag is a
/// speculative addition YAGNI rules out until a real need for one shows up):
/// <list type="bullet">
/// <item><b>Starter</b> — 5&#160;120&#160;MB (5&#160;GB) disk, 5 sites, 2 databases, 3 FTP users. A
/// single small site or two, the smallest useful account.</item>
/// <item><b>Business</b> — 25&#160;600&#160;MB (25&#160;GB) disk, 25 sites, 10 databases, 10 FTP
/// users. An agency running several client sites.</item>
/// <item><b>Unlimited</b> — 1&#160;048&#160;576&#160;MB (1&#160;TB) disk, 500 sites, 500 databases,
/// 100 FTP users. High enough that no real customer hits it in practice, without pretending the
/// server has infinite resources.</item>
/// </list>
/// </summary>
public sealed class PlanSeeder
{
    /// <summary>The Starter plan's stable identity, fixed so re-seeding is idempotent.</summary>
    public static readonly Guid StarterPlanId = Guid.Parse("11111111-0000-4000-8000-000000000001");

    /// <summary>The Business plan's stable identity, fixed so re-seeding is idempotent.</summary>
    public static readonly Guid BusinessPlanId = Guid.Parse("11111111-0000-4000-8000-000000000002");

    /// <summary>The Unlimited plan's stable identity, fixed so re-seeding is idempotent.</summary>
    public static readonly Guid UnlimitedPlanId = Guid.Parse("11111111-0000-4000-8000-000000000003");

    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Creates the seeder with the module's own database context.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    public PlanSeeder(AccountsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Inserts every standard plan that is not already present, keyed by its fixed id. Idempotent:
    /// running this against a database that already holds the standard plans inserts nothing.
    /// </summary>
    /// <param name="cancellationToken">Cancels the seed.</param>
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        foreach (var plan in StandardPlans())
        {
            var alreadySeeded = await _dbContext.Plans
                .AsNoTracking()
                .AnyAsync(p => p.Id == plan.Id, cancellationToken);
            if (!alreadySeeded)
            {
                _dbContext.Plans.Add(plan);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Builds the standard plan set described in this type's own documentation.</summary>
    private static IReadOnlyList<Plan> StandardPlans() =>
    [
        new Plan(StarterPlanId, "PlanStarterName", diskQuotaMb: 5_120, maxSites: 5, maxDatabases: 2, maxFtpUsers: 3),
        new Plan(BusinessPlanId, "PlanBusinessName", diskQuotaMb: 25_600, maxSites: 25, maxDatabases: 10, maxFtpUsers: 10),
        new Plan(UnlimitedPlanId, "PlanUnlimitedName", diskQuotaMb: 1_048_576, maxSites: 500, maxDatabases: 500, maxFtpUsers: 100),
    ];
}
