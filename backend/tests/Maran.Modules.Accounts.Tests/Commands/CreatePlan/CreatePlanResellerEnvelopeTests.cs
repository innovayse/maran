using Maran.Modules.Accounts.Commands.CreatePlan;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.CreatePlan;

/// <summary>
/// Behavioral contract of the per-plan ceilings of a reseller's envelope when it defines a plan: a
/// figure equal to its ceiling is accepted, one over it is refused, and the refusal names the figure.
/// The envelope here equals the base plan figure for figure, so every test moves exactly one of them.
/// </summary>
public sealed class CreatePlanResellerEnvelopeTests : IDisposable
{
    /// <summary>A reseller whose every ceiling equals the base plan's figure.</summary>
    private readonly ResellerPlanScenario _scenario = new(ResellerTestData.Envelope(
        maxDiskQuotaMb: 1_000,
        maxSites: 5,
        maxDatabases: 5,
        maxSftpUsers: 5,
        maxFtpUsers: 5,
        maxCronEntries: 5,
        maxPhpWorkersPerPool: 5,
        maxMailboxes: 10,
        mailboxQuotaPoolMib: 10_240,
        features: AccountFeature.Mail,
        runtimeLimits: RuntimeLimits.Of(
            ResourceLimit.Limited(100), ResourceLimit.Limited(512), ResourceLimit.Limited(50))));

    /// <inheritdoc />
    public void Dispose()
    {
        _scenario.Dispose();
    }

    /// <summary>The base plan: exactly on every ceiling.</summary>
    private static CreatePlanCommand OnTheCeilings()
    {
        return ResellerPlanScenario.Create() with
        {
            CpuQuotaPercent = ResourceCeilingTestValues.Limited(100),
            MemoryMaxMb = ResourceCeilingTestValues.Limited(512),
            MaxProcesses = ResourceCeilingTestValues.Limited(50),
            Features = [AccountFeature.Mail],
        };
    }

    /// <summary>Asserts the command is refused for exactly the named figures and nothing is stored.</summary>
    /// <param name="command">The creation to attempt.</param>
    /// <param name="fields">The figures the refusal must name, in order.</param>
    private async Task AssertRefusedForAsync(
        CreatePlanCommand command,
        params string[] fields)
    {
        var result = await _scenario.CreateAsReseller().HandleAsync(command, CancellationToken.None);

        Assert.Equal("ResellerPlanExceedsEnvelope", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("envelope", result.Extension!.Key);
        Assert.Equal(fields, Assert.IsType<PlanEnvelopeViolationsDto>(result.Extension.Value).Fields);
        Assert.Empty(await _scenario.Seed.Plans.ToListAsync());
        var entry = Assert.Single(_scenario.Audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal(AccountAuditActions.PlanCreated, entry.Action);
    }

    /// <summary>A plan exactly on every ceiling is accepted.</summary>
    [Fact]
    public async Task A_plan_exactly_on_every_ceiling_is_accepted()
    {
        var result = await _scenario.CreateAsReseller().HandleAsync(OnTheCeilings(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A disk quota one over the ceiling is refused naming it.</summary>
    [Fact]
    public async Task A_disk_quota_one_over_the_ceiling_is_refused_naming_it()
    {
        await AssertRefusedForAsync(OnTheCeilings() with { DiskQuotaMb = 1_001 }, "diskQuotaMb");
    }

    /// <summary>A site count one over the ceiling is refused naming it.</summary>
    [Fact]
    public async Task A_site_count_one_over_the_ceiling_is_refused_naming_it()
    {
        await AssertRefusedForAsync(OnTheCeilings() with { MaxSites = 6 }, "maxSites");
    }

    /// <summary>A database count one over the ceiling is refused naming it.</summary>
    [Fact]
    public async Task A_database_count_one_over_the_ceiling_is_refused_naming_it()
    {
        await AssertRefusedForAsync(OnTheCeilings() with { MaxDatabases = 6 }, "maxDatabases");
    }

    /// <summary>An SFTP login count one over the ceiling is refused naming it.</summary>
    [Fact]
    public async Task An_sftp_login_count_one_over_the_ceiling_is_refused_naming_it()
    {
        await AssertRefusedForAsync(OnTheCeilings() with { MaxSftpUsers = 6 }, "maxSftpUsers");
    }

    /// <summary>An FTPS login count one over the ceiling is refused naming it.</summary>
    [Fact]
    public async Task An_ftps_login_count_one_over_the_ceiling_is_refused_naming_it()
    {
        await AssertRefusedForAsync(OnTheCeilings() with { MaxFtpUsers = 6 }, "maxFtpUsers");
    }

    /// <summary>A cron entry count one over the ceiling is refused naming it.</summary>
    [Fact]
    public async Task A_cron_entry_count_one_over_the_ceiling_is_refused_naming_it()
    {
        await AssertRefusedForAsync(OnTheCeilings() with { MaxCronEntries = 6 }, "maxCronEntries");
    }

    /// <summary>A worker count one over the ceiling is refused naming it.</summary>
    [Fact]
    public async Task A_worker_count_one_over_the_ceiling_is_refused_naming_it()
    {
        await AssertRefusedForAsync(OnTheCeilings() with { MaxPhpWorkersPerPool = 6 }, "maxPhpWorkersPerPool");
    }

    /// <summary>A mailbox count one over the ceiling is refused naming it.</summary>
    [Fact]
    public async Task A_mailbox_count_one_over_the_ceiling_is_refused_naming_it()
    {
        await AssertRefusedForAsync(
            OnTheCeilings() with { MailLimits = new MailLimitsDto(11, 1_024, 100, 10_240) }, "maxMailboxes");
    }

    /// <summary>A mailbox quota pool one over the ceiling is refused naming it.</summary>
    [Fact]
    public async Task A_mailbox_quota_pool_one_over_the_ceiling_is_refused_naming_it()
    {
        await AssertRefusedForAsync(
            OnTheCeilings() with { MailLimits = new MailLimitsDto(10, 1_024, 100, 10_241) }, "mailboxQuotaPoolMib");
    }

    /// <summary>A plan stating mail figures exactly on the ceilings is accepted.</summary>
    [Fact]
    public async Task A_plan_stating_mail_figures_exactly_on_the_ceilings_is_accepted()
    {
        var result = await _scenario.CreateAsReseller().HandleAsync(
            OnTheCeilings() with { MailLimits = new MailLimitsDto(10, 1_024, 100, 10_240) }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A feature the reseller was not sold is refused naming the feature set.</summary>
    [Fact]
    public async Task A_feature_the_reseller_was_not_sold_is_refused_naming_the_feature_set()
    {
        await AssertRefusedForAsync(
            OnTheCeilings() with { Features = [AccountFeature.Mail, AccountFeature.Dns] }, "features");
    }

    /// <summary>A CPU ceiling one over the envelope's is refused naming it.</summary>
    [Fact]
    public async Task A_cpu_ceiling_one_over_the_envelopes_is_refused_naming_it()
    {
        await AssertRefusedForAsync(
            OnTheCeilings() with { CpuQuotaPercent = ResourceCeilingTestValues.Limited(101) }, "cpuQuotaPercent");
    }

    /// <summary>A memory ceiling one over the envelope's is refused naming it.</summary>
    [Fact]
    public async Task A_memory_ceiling_one_over_the_envelopes_is_refused_naming_it()
    {
        await AssertRefusedForAsync(
            OnTheCeilings() with { MemoryMaxMb = ResourceCeilingTestValues.Limited(513) }, "memoryMaxMb");
    }

    /// <summary>A process ceiling one over the envelope's is refused naming it.</summary>
    [Fact]
    public async Task A_process_ceiling_one_over_the_envelopes_is_refused_naming_it()
    {
        await AssertRefusedForAsync(
            OnTheCeilings() with { MaxProcesses = ResourceCeilingTestValues.Limited(51) }, "maxProcesses");
    }

    /// <summary>A plan that states no cap is over a ceiling that exists.</summary>
    [Fact]
    public async Task A_plan_that_states_no_cap_is_over_a_ceiling_that_exists()
    {
        await AssertRefusedForAsync(
            OnTheCeilings() with { CpuQuotaPercent = ResourceCeilingTestValues.Unlimited }, "cpuQuotaPercent");
    }

    /// <summary>Every figure that is over is named, not just the first.</summary>
    [Fact]
    public async Task Every_figure_that_is_over_is_named_not_just_the_first()
    {
        await AssertRefusedForAsync(
            OnTheCeilings() with { DiskQuotaMb = 2_000, MaxSites = 9, MaxProcesses = ResourceCeilingTestValues.Limited(99) },
            "diskQuotaMb",
            "maxSites",
            "maxProcesses");
    }

    /// <summary>An administrator is held to no envelope, whatever it asks for.</summary>
    [Fact]
    public async Task An_administrator_is_held_to_no_envelope()
    {
        var result = await _scenario.CreateAsAdministrator().HandleAsync(
            OnTheCeilings() with { DiskQuotaMb = 999_999, MaxSites = 999 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
