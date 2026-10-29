using Maran.Modules.Accounts.Commands.DeletePlan;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Maran.Modules.Accounts.Tests.Commands.DeletePlan;

/// <summary>
/// Behavioral contract of <see cref="DeletePlanCommandHandler"/>. An account reads every one of its
/// limits through its plan, so deleting a plan accounts still sit on would leave them with none; the
/// handler refuses it by name, and the database's <c>ON DELETE RESTRICT</c> foreign key is only the
/// backstop (proved against real PostgreSQL in the host integration tests).
/// </summary>
public sealed class DeletePlanCommandHandlerTests : IDisposable
{
    /// <summary>Throws an arranged exception on the next save, to force a race the in-memory store cannot.</summary>
    private readonly ArmedSaveFailureInterceptor _saveFailure = new();

    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>Opens the context with the save-failure interceptor attached.</summary>
    public DeletePlanCommandHandlerTests()
    {
        _dbContext = AccountsTestContext.Create(_saveFailure);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Builds the handler over this test's context and journal.</summary>
    private DeletePlanCommandHandler NewHandler()
    {
        return new DeletePlanCommandHandler(_dbContext, new AccountAuditJournal(_audit, FakeCurrentUser.Admin()));
    }

    /// <summary>Builds a deletion request for <paramref name="planId"/>.</summary>
    /// <param name="planId">The plan to delete.</param>
    private static DeletePlanCommand Command(Guid planId)
    {
        return new(planId, "203.0.113.7", "unit-tests");
    }

    /// <summary>Stores an operator's plan.</summary>
    /// <param name="name">The plan's name.</param>
    private async Task<Plan> AddPlanAsync(string name)
    {
        var plan = Plan.Define(Guid.NewGuid(), name, 10_240, 10, 5, 4, 2, 3, 8);
        _dbContext.Plans.Add(plan);
        await _dbContext.SaveChangesAsync();
        return plan;
    }

    /// <summary>Puts an account on <paramref name="plan"/>.</summary>
    /// <param name="plan">The plan the account sits on.</param>
    private async Task AddAccountAsync(Plan plan)
    {
        _dbContext.Accounts.Add(new Account(
            Guid.NewGuid(), "olive", "olive.example.com", plan.Id, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>A plan no account is on is deleted and the deletion is journalled as a success.</summary>
    [Fact]
    public async Task A_plan_no_account_is_on_is_deleted_and_the_deletion_is_journalled_as_a_success()
    {
        var plan = await AddPlanAsync("Agency");

        var result = await NewHandler().HandleAsync(Command(plan.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await _dbContext.Plans.ToListAsync());
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.PlanDeleted, entry.Action);
        Assert.Equal("Agency", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        Assert.Equal("unit-tests", entry.UserAgent);
    }

    /// <summary>A plan an account is still on is refused, kept, and journalled as a failure.</summary>
    [Fact]
    public async Task A_plan_an_account_is_still_on_is_refused_kept_and_journalled_as_a_failure()
    {
        var plan = await AddPlanAsync("Agency");
        await AddAccountAsync(plan);

        var result = await NewHandler().HandleAsync(Command(plan.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("PlanInUse", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Single(await _dbContext.Plans.ToListAsync());
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.PlanDeleted, entry.Action);
        Assert.Equal("Agency", entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>Accounts on another plan do not stop an unused plan being deleted.</summary>
    [Fact]
    public async Task Accounts_on_another_plan_do_not_stop_an_unused_plan_being_deleted()
    {
        // The inverse control for the refusal above: a handler that refused whenever ANY account
        // existed would pass that test and make every plan undeletable on a panel with customers.
        var busy = await AddPlanAsync("Busy");
        var idle = await AddPlanAsync("Idle");
        await AddAccountAsync(busy);

        var result = await NewHandler().HandleAsync(Command(idle.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var remaining = Assert.Single(await _dbContext.Plans.ToListAsync());
        Assert.Equal(busy.Id, remaining.Id);
    }

    /// <summary>A plan that does not exist is refused as not found and journalled with the id probed.</summary>
    [Fact]
    public async Task A_plan_that_does_not_exist_is_refused_as_not_found_and_journalled_with_the_id_probed()
    {
        var missing = Guid.NewGuid();

        var result = await NewHandler().HandleAsync(Command(missing), CancellationToken.None);

        Assert.Equal("PlanNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(missing.ToString("D"), entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>Once the last account leaves, the plan can be deleted.</summary>
    [Fact]
    public async Task Once_the_last_account_leaves_the_plan_can_be_deleted()
    {
        var plan = await AddPlanAsync("Agency");
        await AddAccountAsync(plan);
        await NewHandler().HandleAsync(Command(plan.Id), CancellationToken.None);
        _dbContext.Accounts.RemoveRange(await _dbContext.Accounts.ToListAsync());
        await _dbContext.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(plan.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await _dbContext.Plans.ToListAsync());
    }

    /// <summary>Builds the failure the database raises, wrapped as EF Core wraps it.</summary>
    /// <param name="sqlState">The SQLSTATE the database reports.</param>
    private static DbUpdateException Violation(string sqlState)
    {
        return new DbUpdateException(
            "save failed", new PostgresException("constraint violated", "ERROR", "ERROR", sqlState));
    }

    /// <summary>An account placed on the plan between the check and the delete is answered PlanInUse and journalled as a failure.</summary>
    [Fact]
    public async Task An_account_placed_on_the_plan_between_the_check_and_the_delete_answers_plan_in_use_and_is_journalled_as_a_failure()
    {
        var plan = await AddPlanAsync("Agency");
        _saveFailure.Next = Violation(PostgresErrorCodes.ForeignKeyViolation);

        var result = await NewHandler().HandleAsync(Command(plan.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("PlanInUse", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.PlanDeleted, entry.Action);
        Assert.Equal("Agency", entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A database failure that is not a foreign-key violation propagates instead of being called in use.</summary>
    [Fact]
    public async Task A_delete_failing_for_any_other_database_reason_propagates()
    {
        // The inverse control: a catch that swallowed every DbUpdateException would pass the test above.
        var plan = await AddPlanAsync("Agency");
        _saveFailure.Next = Violation(PostgresErrorCodes.UniqueViolation);

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await NewHandler().HandleAsync(Command(plan.Id), CancellationToken.None);
        });

        Assert.Empty(_audit.Entries);
    }
}
