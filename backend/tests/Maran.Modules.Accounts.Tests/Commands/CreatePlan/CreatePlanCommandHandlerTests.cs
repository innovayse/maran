using Maran.Modules.Accounts.Commands.CreatePlan;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.CreatePlan;

/// <summary>
/// Behavioral contract of <see cref="CreatePlanCommandHandler"/>: an operator defining what they
/// sell. The name is the plan's identity to every customer choosing between plans, so the refusals
/// that keep two plans from reading the same are pinned as hard as the success.
/// </summary>
public sealed class CreatePlanCommandHandlerTests : IDisposable
{
    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The display names of the plans the panel ships, as the request's culture shows them.</summary>
    private readonly StubDisplayNames _displayNames = new StubDisplayNames().With("PlanStarterName", "Starter");

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Builds the handler over this test's context and journal.</summary>
    private CreatePlanCommandHandler NewHandler()
    {
        return new CreatePlanCommandHandler(
            _dbContext,
            new PlanNameDirectory(_dbContext, _displayNames),
            new AccountAuditJournal(_audit, FakeCurrentUser.Admin()),
            _displayNames);
    }

    /// <summary>Builds a creation request for <paramref name="name"/>.</summary>
    /// <param name="name">The plan name to ask for.</param>
    private static CreatePlanCommand Command(string name = "Agency")
    {
        return new(name, 10_240, 10, 5, 4, 2, 3, 8, IpAddress: "203.0.113.7", UserAgent: "unit-tests");
    }

    /// <summary>A created plan is stored with the limits it was asked for and returned with none of its accounts.</summary>
    [Fact]
    public async Task A_created_plan_is_stored_with_the_limits_it_was_asked_for_and_returned_with_no_accounts()
    {
        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await _dbContext.Plans.SingleAsync();
        Assert.Equal(stored.Id, result.Value.Id);
        Assert.Equal("Agency", result.Value.DisplayName);
        Assert.Equal(0, result.Value.AccountCount);
        // Each of these numbers differs from every other one passed, so an argument list that fed a
        // property from its neighbour fails here rather than passing on a plausible-looking value.
        Assert.Equal(10_240, stored.DiskQuotaMb);
        Assert.Equal(10, stored.MaxSites);
        Assert.Equal(5, stored.MaxDatabases);
        Assert.Equal(4, stored.MaxSftpUsers);
        Assert.Equal(2, stored.MaxFtpUsers);
        Assert.Equal(3, stored.MaxCronEntries);
        Assert.Equal(8, stored.MaxPhpWorkersPerPool);
    }

    /// <summary>A created plan keeps its name in both the name column and the key column.</summary>
    [Fact]
    public async Task A_created_plan_keeps_its_name_in_both_the_name_column_and_the_key_column()
    {
        // The previous release reads only the key column, so a plan written here must still be
        // named for it; the database also carries a constraint that says the two are equal.
        await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var stored = await _dbContext.Plans.SingleAsync();
        Assert.Equal("Agency", stored.Name);
        Assert.Equal("Agency", stored.DisplayNameKey);
    }

    /// <summary>The name is stored without the white space around it.</summary>
    [Fact]
    public async Task The_name_is_stored_without_the_white_space_around_it()
    {
        // Otherwise " Agency" and "Agency" would be two plans that look identical in every list.
        var result = await NewHandler().HandleAsync(Command("  Agency  "), CancellationToken.None);

        Assert.Equal("Agency", result.Value.DisplayName);
        Assert.Equal("Agency", (await _dbContext.Plans.SingleAsync()).Name);
    }

    /// <summary>A created plan is journalled as a success naming the plan and the caller.</summary>
    [Fact]
    public async Task A_created_plan_is_journalled_as_a_success_naming_the_plan_and_the_caller()
    {
        await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.PlanCreated, entry.Action);
        Assert.Equal("Agency", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        Assert.Equal("unit-tests", entry.UserAgent);
    }

    /// <summary>A name another plan already has is refused whatever its case, and nothing is stored.</summary>
    [Theory]
    [InlineData("Agency")]
    [InlineData("agency")]
    [InlineData("AGENCY")]
    public async Task A_name_another_plan_already_has_is_refused_whatever_its_case_and_nothing_is_stored(string name)
    {
        await NewHandler().HandleAsync(Command(), CancellationToken.None);
        _audit.Entries.Clear();

        var result = await NewHandler().HandleAsync(Command(name), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("PlanNameTaken", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(1, await _dbContext.Plans.CountAsync());
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.PlanCreated, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A name a shipped plan shows in the request's language is refused.</summary>
    [Fact]
    public async Task A_name_a_shipped_plan_shows_in_the_requests_language_is_refused()
    {
        // The shipped plan's row stores a resource key, not the text a customer reads, so a check on
        // the column alone would let a second "Starter" in — two plans a customer cannot tell apart.
        _dbContext.Plans.Add(new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, 5, 5));
        await _dbContext.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command("Starter"), CancellationToken.None);

        Assert.Equal("PlanNameTaken", result.Error!.Code);
        Assert.Equal(1, await _dbContext.Plans.CountAsync());
    }

    /// <summary>A name equal to a shipped plan's stored key is refused with a sentence, not a constraint.</summary>
    [Fact]
    public async Task A_name_equal_to_a_shipped_plans_stored_key_is_refused_with_a_sentence_not_a_constraint()
    {
        // The unique index is on the key column; a name typed as the key would otherwise reach it
        // and come back as a database exception rather than a conflict the operator can read.
        _dbContext.Plans.Add(new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, 5, 5));
        await _dbContext.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command("PlanStarterName"), CancellationToken.None);

        Assert.Equal("PlanNameTaken", result.Error!.Code);
    }

    /// <summary>A name no plan has is accepted beside the plans that exist.</summary>
    [Fact]
    public async Task A_name_no_plan_has_is_accepted_beside_the_plans_that_exist()
    {
        // The inverse control for the refusals above: a check that refused every name would pass
        // all of them.
        _dbContext.Plans.Add(new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, 5, 5));
        await _dbContext.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command("Agency"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, await _dbContext.Plans.CountAsync());
    }
}
