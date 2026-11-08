using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Persistence;

namespace Maran.Modules.Accounts.Seeders;

/// <summary>
/// Seeds the standard plans a fresh installation ships with, so there is something to create an
/// account against before an operator has defined any plan of their own. Three tiers — a starter, a
/// business, and a top tier with a generous-but-finite ceiling rather than a literal "unlimited"
/// (this pass keeps every limit a plain, comparable integer; an explicit unlimited flag is a
/// speculative addition YAGNI rules out until a real need for one shows up).
///
/// The worker figure on each tier is per POOL (see <see cref="Plan.MaxPhpWorkersPerPool"/>), not a
/// per-account total: an account owns one pool per PHP version it uses, so the tier ceiling is the
/// number below multiplied by the versions in use, not by the site count. At a conservative 50 MB
/// per worker, 5/10/20 is 250&#160;MB, 500&#160;MB and 1&#160;GB of resident memory per pool.
/// <list type="bullet">
/// <item><b>Starter</b> — 5&#160;120&#160;MB (5&#160;GB) disk, 5 sites, 2 databases, 3 SFTP logins,
/// 3 FTPS logins, 5 cron entries. A single small site or two, the smallest useful account.</item>
/// <item><b>Business</b> — 25&#160;600&#160;MB (25&#160;GB) disk, 25 sites, 10 databases, 10 SFTP
/// logins, 10 FTPS logins, 20 cron entries. An agency running several client sites.</item>
/// <item><b>Unlimited</b> — 1&#160;048&#160;576&#160;MB (1&#160;TB) disk, 500 sites, 500 databases,
/// 100 SFTP logins, 100 FTPS logins, 200 cron entries. High enough that no real customer hits it in
/// practice, without pretending the server has infinite resources.</item>
/// </list>
///
/// The PHP bounds of the tiers (issue #138, owner decision O2) climb with the tier, as the worker figures do:
/// <list type="bullet">
/// <item><b>Starter</b> — 128&#160;MiB memory, 32&#160;MiB uploads, 60&#160;s, 1&#160;000 input variables.</item>
/// <item><b>Business</b> — 256&#160;MiB, 64&#160;MiB, 120&#160;s, 3&#160;000: <see cref="PhpLimits.Default"/>.</item>
/// <item><b>Unlimited</b> — 512&#160;MiB, 128&#160;MiB, 300&#160;s, 10&#160;000: the agent's own ceilings for
/// memory, execution time and input variables, which no plan may exceed.</item>
/// </list>
/// They are written only when a fresh panel is seeded, so a plan an operator edited keeps its bounds.
///
/// The mail allowance of the tiers (issue #177, plan decision D13) climbs the same way, as mailboxes,
/// the quota a new mailbox gets, messages per mailbox per hour and the pool of all the quotas:
/// <list type="bullet">
/// <item><b>Starter</b> — 5 mailboxes, 1&#160;024&#160;MiB each by default, 100 an hour, a 5&#160;120&#160;MiB pool.</item>
/// <item><b>Business</b> — 10, 1&#160;024&#160;MiB, 100, 10&#160;240&#160;MiB: <see cref="MailLimits.Default"/>.</item>
/// <item><b>Unlimited</b> — 100, 2&#160;048&#160;MiB, 500, 204&#160;800&#160;MiB.</item>
/// </list>
///
/// Every shipped tier includes every feature (<see cref="Maran.Sdk.Contracts.AccountFeature.All"/>), by
/// the <see cref="Plan"/> constructor's default: a customer on a shipped plan loses nothing, and an
/// operator who wants a tier without a feature edits it or defines their own plan.
///
/// The FTPS allowance of each tier equals its SFTP one, and that is a product decision rather than a
/// derivation: the two are separate limits on separate daemons (<see cref="Plan.MaxFtpUsers"/>), and
/// nothing stops an operator selling different numbers. Equal is what the shipped tiers mean —
/// "this many file-transfer logins, over whichever protocol you prefer" — and it is the choice that
/// surprises nobody who reads the plan's SFTP figure first.
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
    /// Inserts the standard plans into a panel that has no plan at all, keyed by their fixed ids.
    /// Idempotent: running this against a database that already holds any plan inserts nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only into a panel with no plans, not "whichever standard plan is absent".</b> The seeder used
    /// to top the three back up on every start, which was harmless while nothing could remove a plan.
    /// Now an administrator can delete one, and a deletion that returns at the next restart is a plan
    /// the operator retired still being on sale. "A fresh installation ships with these" is the
    /// promise; a panel that holds any plan is not fresh, so a deleted standard plan stays deleted
    /// and an edited one stays edited. The cost is that an operator who deletes every plan and
    /// restarts has the three standard ones back — the same state as a new installation, and no
    /// account could be created in the plan-less one anyway.
    /// </para>
    /// <para>
    /// <b>The existence read bypasses the plan filter explicitly.</b> The seeder runs at start-up as
    /// the anonymous caller, and the context shows an anonymous caller no plan at all, so a filtered
    /// read would answer "none" on every boot and the insert would collide with the fixed ids. The
    /// bypass is the only read here; the rest of the method only adds rows.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the seed.</param>
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // seeding runs as AnonymousCaller, who sees no plan; the check must see every plan
        var panelHoldsPlans = await _dbContext.Plans
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(cancellationToken);
#pragma warning restore RS0030

        if (panelHoldsPlans)
        {
            return;
        }

        foreach (var plan in StandardPlans())
        {
            _dbContext.Plans.Add(plan);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Builds the standard plan set described in this type's own documentation.</summary>
    private static IReadOnlyList<Plan> StandardPlans()
    {
        var starter = new Plan(
            StarterPlanId, "PlanStarterName",
            diskQuotaMb: 5_120, maxSites: 5, maxDatabases: 2, maxSftpUsers: 3, maxCronEntries: 5,
            maxPhpWorkersPerPool: 5, maxFtpUsers: 3);
        starter.ChangePhpLimits(PhpLimits.Of(
            memoryLimitMaxMb: 128, uploadMaxMb: 32, maxExecutionSeconds: 60, maxInputVars: 1_000));
        starter.ChangeMailLimits(MailLimits.Of(
            maxMailboxes: 5, defaultMailboxQuotaMib: 1_024, mailboxSendLimitPerHour: 100, mailboxQuotaPoolMib: 5_120));

        // The middle tier's bounds are PhpLimits.Default: what a plan that predates the bounds reads as.
        var business = new Plan(
            BusinessPlanId, "PlanBusinessName",
            diskQuotaMb: 25_600, maxSites: 25, maxDatabases: 10, maxSftpUsers: 10, maxCronEntries: 20,
            maxPhpWorkersPerPool: 10, maxFtpUsers: 10);
        business.ChangePhpLimits(PhpLimits.Of(
            memoryLimitMaxMb: 256, uploadMaxMb: 64, maxExecutionSeconds: 120, maxInputVars: 3_000));

        // Likewise the middle tier's mail allowance is MailLimits.Default.
        business.ChangeMailLimits(MailLimits.Of(
            maxMailboxes: 10, defaultMailboxQuotaMib: 1_024, mailboxSendLimitPerHour: 100, mailboxQuotaPoolMib: 10_240));

        var unlimited = new Plan(
            UnlimitedPlanId, "PlanUnlimitedName",
            diskQuotaMb: 1_048_576, maxSites: 500, maxDatabases: 500, maxSftpUsers: 100, maxCronEntries: 200,
            maxPhpWorkersPerPool: 20, maxFtpUsers: 100);
        unlimited.ChangePhpLimits(PhpLimits.Of(
            memoryLimitMaxMb: 512, uploadMaxMb: 128, maxExecutionSeconds: 300, maxInputVars: 10_000));
        unlimited.ChangeMailLimits(MailLimits.Of(
            maxMailboxes: 100, defaultMailboxQuotaMib: 2_048, mailboxSendLimitPerHour: 500, mailboxQuotaPoolMib: 204_800));

        return [starter, business, unlimited];
    }
}
