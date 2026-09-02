using Maran.Modules.Accounts.Domain;

namespace Maran.Modules.Accounts.Tests.Domain;

/// <summary>Behavioral contract of the <see cref="Plan"/> entity.</summary>
public sealed class PlanTests
{
    /// <summary>A plan with a non positive worker budget cannot be created.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_plan_with_a_non_positive_worker_budget_cannot_be_created(int workers)
    {
        // The budget becomes pm.max_children, and php-fpm refuses to start a pool with a
        // non-positive one — so a plan carrying zero is a plan whose sites cannot serve PHP. The
        // boundary refuses it here rather than letting it reach a rendered config, where the only
        // symptom is a pool that will not start.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, workers);
        });
    }

    /// <summary>A plan with a non positive database allowance cannot be created.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_plan_with_a_non_positive_database_allowance_cannot_be_created(int databases)
    {
        // A plan sold with a zero allowance refuses every database creation, and the refusal names
        // the plan rather than the mistake — so the account looks broken and the plan looks fine.
        // The previous plan shipped a migration that backfilled zero into a limit column; this
        // boundary is what stops the same value ever being written deliberately.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, databases, 3, 5);
        });
    }

    /// <summary>A plan with a non positive sftp allowance cannot be created.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_plan_with_a_non_positive_sftp_allowance_cannot_be_created(int sftpUsers)
    {
        // The same refusal the database allowance gets, and it bites harder: an account with no SFTP
        // login has no way to put files on its own sites at all, so a zero here sells a plan whose
        // sites can never be filled — while the refusal names the plan rather than the mistake.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, sftpUsers, 5);
        });
    }

    /// <summary>A plan with a positive worker budget is created.</summary>
    [Fact]
    public void A_plan_with_a_positive_worker_budget_is_created()
    {
        var plan = new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, 7);

        Assert.Equal(7, plan.MaxPhpWorkersPerPool);
    }

    /// <summary>A plan with a positive database allowance is created.</summary>
    [Fact]
    public void A_plan_with_a_positive_database_allowance_is_created()
    {
        // Guards the refusal above from passing because the constructor throws for some other
        // reason: a guard that rejects everything is not a guard.
        var plan = new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 4, 3, 7);

        Assert.Equal(4, plan.MaxDatabases);
    }

    /// <summary>A plan with a positive sftp allowance is created.</summary>
    [Fact]
    public void A_plan_with_a_positive_sftp_allowance_is_created()
    {
        // Guards the refusal above from passing because the constructor throws for some other
        // reason: a guard that rejects everything is not a guard.
        var plan = new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 4, 6, 7);

        Assert.Equal(6, plan.MaxSftpUsers);
    }
}
