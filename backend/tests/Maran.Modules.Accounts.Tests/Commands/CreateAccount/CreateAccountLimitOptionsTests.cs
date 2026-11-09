using Maran.Agent.Client.Services.AccountsService;
using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.CreateAccount;

/// <summary>
/// What an account is created with beyond its name, domain and plan (issue #194): the inode, process
/// and open-files ceilings it states for itself, the login shell, how the owner gets a way in, and the
/// backup choice. Asserted on the agent's recorded calls, the stored row, the event the owner's login
/// is made from and the journal.
/// </summary>
public sealed class CreateAccountLimitOptionsTests : IDisposable
{
    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <summary>The agent double.</summary>
    private readonly RecordingAgentAccountsClient _agent = new();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The bus the owner's login is requested on.</summary>
    private readonly StubMessageBus _bus = new();

    /// <summary>A plan with a CPU ceiling and a ceiling of 64 processes.</summary>
    private readonly Plan _plan = Plan.Define(Guid.NewGuid(), "Agency", 1_024, 5, 2, 3, 0, 5, 5);

    /// <summary>Stores the plan.</summary>
    public CreateAccountLimitOptionsTests()
    {
        _plan.ChangeRuntimeLimits(RuntimeLimits.Of(
            ResourceLimit.Limited(150), ResourceLimit.Unlimited(), ResourceLimit.Limited(64)));
        _dbContext.Plans.Add(_plan);
        _dbContext.SaveChanges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Runs the creation of <c>acme</c> as <paramref name="caller"/>.</summary>
    /// <param name="command">The command to run; its name, domain, plan and owner address are filled in.</param>
    /// <param name="caller">The caller; an administrator when not stated.</param>
    /// <returns>The handler's result.</returns>
    private Task<Result<AccountDto>> RunAsync(Func<CreateAccountCommand, CreateAccountCommand> command, ICurrentUser? caller = null)
    {
        var who = caller ?? FakeCurrentUser.Admin();
        var handler = AccountHandlerFactory.Create(_dbContext, who, _agent, _audit, _bus);
        var begin = new CreateAccountCommand(
            "acme", "acme.example.com", _plan.Id, "owner@example.com", IpAddress: "203.0.113.7", UserAgent: "unit-tests");

        return handler.HandleAsync(command(begin), CancellationToken.None);
    }

    /// <summary>An account created with the defaults is given no shell, no inode ceiling and no open-files push.</summary>
    [Fact]
    public async Task An_account_created_with_the_defaults_has_no_shell_no_inode_ceiling_and_no_open_files_push()
    {
        var result = await RunAsync(command => { return command; });

        Assert.True(result.IsSuccess);
        var provisioning = Assert.Single(_agent.Provisioned);
        Assert.Null(provisioning.InodeLimit);
        Assert.Equal(AccountLoginShell.Nologin, provisioning.Shell);
        Assert.DoesNotContain(_agent.Calls, call => { return call.StartsWith("open-files:", StringComparison.Ordinal); });
        var stored = await _dbContext.Accounts.SingleAsync();
        Assert.False(stored.ShellAccess);
        Assert.True(stored.LimitOverrides.IsEmpty);
        Assert.Equal("acme", Assert.Single(_audit.Entries).Subject);
    }

    /// <summary>The ceilings an administrator states are stored, provisioned and pushed in place of the plan's.</summary>
    [Fact]
    public async Task The_ceilings_an_administrator_states_are_stored_provisioned_and_pushed_in_place_of_the_plans()
    {
        var result = await RunAsync(command =>
        {
            return command with { InodeLimit = 50_000, ProcessLimit = 500, OpenFilesLimit = 2_048, ShellAccess = true };
        });

        Assert.True(result.IsSuccess);
        var provisioning = Assert.Single(_agent.Provisioned);
        Assert.Equal(50_000UL, provisioning.InodeLimit);
        Assert.Equal(AccountLoginShell.Bash, provisioning.Shell);
        Assert.Contains("limits:acme:Limited150/Unlimited0/Limited500", _agent.Calls);
        Assert.Equal(("acme", (ulong?)2_048UL), Assert.Single(_agent.OpenFilesLimits));
        var stored = await _dbContext.Accounts.SingleAsync();
        Assert.True(stored.ShellAccess);
        Assert.Equal(50_000, stored.LimitOverrides.InodeLimit);
        Assert.Equal(500, stored.LimitOverrides.ProcessLimit);
        Assert.Equal(2_048, stored.LimitOverrides.OpenFilesLimit);
    }

    /// <summary>The wire's zero for inodes and open files means no limit: nothing is stored or pushed for it.</summary>
    [Fact]
    public async Task The_wires_zero_for_inodes_and_open_files_means_no_limit_and_nothing_is_stored_or_pushed()
    {
        var result = await RunAsync(command => { return command with { InodeLimit = 0, OpenFilesLimit = 0 }; });

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(_agent.Provisioned).InodeLimit);
        Assert.Empty(_agent.OpenFilesLimits);
        Assert.True((await _dbContext.Accounts.SingleAsync()).LimitOverrides.IsEmpty);
    }

    /// <summary>A reseller stating a process ceiling above its plan's is refused before anything reaches the host.</summary>
    [Fact]
    public async Task A_reseller_stating_a_process_ceiling_above_its_plans_is_refused_before_anything_reaches_the_host()
    {
        using var world = new ResellerAccountsWorld();
        var seeding = world.ContextFor(FakeCurrentUser.Admin());
        var resellerPlan = await seeding.Plans.SingleAsync(p => p.Id == world.PlanA.Id);
        resellerPlan.ChangeRuntimeLimits(RuntimeLimits.Of(
            ResourceLimit.Unlimited(), ResourceLimit.Unlimited(), ResourceLimit.Limited(64)));
        await seeding.SaveChangesAsync();
        var handler = AccountHandlerFactory.Create(world.ContextFor(world.UserA), world.UserA, _agent, _audit, _bus);

        var result = await handler.HandleAsync(
            new CreateAccountCommand(
                "newcomer", "newcomer.example.com", world.PlanA.Id, "o@example.com", ProcessLimit: 65,
                IpAddress: "203.0.113.7", UserAgent: "unit-tests"),
            CancellationToken.None);

        Assert.Equal("AccountLimitAbovePlan", result.Error!.Code);
        Assert.Empty(_agent.Calls);
        Assert.False(await world.ContextFor(FakeCurrentUser.Admin()).Accounts.AnyAsync(a => a.Name == "newcomer"));
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>A refused open-files ceiling removes the system user again and stores no row.</summary>
    [Fact]
    public async Task A_refused_open_files_ceiling_removes_the_system_user_again_and_stores_no_row()
    {
        _agent.OpenFilesRefusals["acme"] = Error.Of("AgentSystemFailure", ErrorType.Failure);

        var result = await RunAsync(command => { return command with { OpenFilesLimit = 1_024 }; });

        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Equal("delete:acme", _agent.Calls[^1]);
        Assert.Empty(await _dbContext.Accounts.ToListAsync());
    }

    /// <summary>The owner's login is announced with the mode, the password and the backup choice.</summary>
    [Fact]
    public async Task The_owners_login_is_announced_with_the_mode_the_password_and_the_backup_choice()
    {
        var result = await RunAsync(command =>
        {
            return command with
            {
                LoginMode = OwnerLoginMode.InitialPassword,
                InitialPassword = new SensitiveString("Correct-Horse-9"),
                BackupEnabled = false,
            };
        });

        Assert.True(result.IsSuccess);
        var announced = Assert.IsType<AccountCreated>(Assert.Single(_bus.Invoked));
        Assert.Equal(OwnerLoginMode.InitialPassword, announced.LoginMode);
        Assert.Equal("Correct-Horse-9", announced.InitialPassword!.Reveal());
        Assert.False(announced.BackupEnabled);
    }

    /// <summary>An invitation is the default: the event carries no password and backups stay on.</summary>
    [Fact]
    public async Task An_invitation_is_the_default_and_the_event_carries_no_password_and_backups_stay_on()
    {
        await RunAsync(command => { return command; });

        var announced = Assert.IsType<AccountCreated>(Assert.Single(_bus.Invoked));
        Assert.Equal(OwnerLoginMode.Invitation, announced.LoginMode);
        Assert.Null(announced.InitialPassword);
        Assert.True(announced.BackupEnabled);
    }

    /// <summary>The journal lists every non-default option and never the password.</summary>
    [Fact]
    public async Task The_journal_lists_every_non_default_option_and_never_the_password()
    {
        await RunAsync(command =>
        {
            return command with
            {
                LoginMode = OwnerLoginMode.InitialPassword,
                InitialPassword = new SensitiveString("Correct-Horse-9"),
                ShellAccess = true,
                BackupEnabled = false,
                InodeLimit = 50_000,
                ProcessLimit = 500,
                OpenFilesLimit = 2_048,
            };
        });

        var entry = Assert.Single(_audit.Entries);
        Assert.True(entry.Succeeded);
        Assert.Equal(
            "acme [login: initial password, shell, no backups, inodes: 50000, processes: 500, open files: 2048]",
            entry.Subject);
        Assert.DoesNotContain("Correct-Horse-9", entry.Subject, StringComparison.Ordinal);
    }
}
