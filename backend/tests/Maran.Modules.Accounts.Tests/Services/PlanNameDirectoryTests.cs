using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>
/// Behavioral contract of <see cref="PlanNameDirectory"/>: whether a name is already some plan's, as
/// a customer choosing between plans would read it. Two plans that read the same are
/// indistinguishable to the person buying, so the comparison is on the displayed text.
/// </summary>
public sealed class PlanNameDirectoryTests : IDisposable
{
    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <summary>The directory under test, with the Starter plan shown as "Starter".</summary>
    private readonly PlanNameDirectory _directory;

    /// <summary>The operator's plan the exclusion tests edit.</summary>
    private readonly Plan _agency = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);

    /// <summary>Stores a shipped plan and an operator's plan and builds the directory over them.</summary>
    public PlanNameDirectoryTests()
    {
        _dbContext.Plans.AddRange(new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, 5, 5), _agency);
        _dbContext.SaveChanges();
        _directory = new PlanNameDirectory(_dbContext, new StubDisplayNames().With("PlanStarterName", "Starter"));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>An operator's name is taken whatever its case.</summary>
    [Theory]
    [InlineData("Agency")]
    [InlineData("AGENCY")]
    public async Task An_operators_name_is_taken_whatever_its_case(string name)
    {
        Assert.True(await _directory.IsTakenAsync(name, null, CancellationToken.None));
    }

    /// <summary>A shipped plan's displayed name and its stored key are both taken.</summary>
    [Theory]
    [InlineData("Starter")]
    [InlineData("starter")]
    [InlineData("PlanStarterName")]
    public async Task A_shipped_plans_displayed_name_and_its_stored_key_are_both_taken(string name)
    {
        Assert.True(await _directory.IsTakenAsync(name, null, CancellationToken.None));
    }

    /// <summary>A name no plan reads as is free.</summary>
    [Fact]
    public async Task A_name_no_plan_reads_as_is_free()
    {
        // The inverse control: a directory that answered "taken" for everything would pass the above.
        Assert.False(await _directory.IsTakenAsync("Studio", null, CancellationToken.None));
    }

    /// <summary>A plan's own name is free for that plan and taken for every other.</summary>
    [Fact]
    public async Task A_plans_own_name_is_free_for_that_plan_and_taken_for_every_other()
    {
        Assert.False(await _directory.IsTakenAsync("Agency", _agency.Id, CancellationToken.None));
        Assert.True(await _directory.IsTakenAsync("Agency", Guid.NewGuid(), CancellationToken.None));
    }
}
