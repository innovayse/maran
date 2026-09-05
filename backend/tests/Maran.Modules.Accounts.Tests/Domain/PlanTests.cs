using Maran.Modules.Accounts.Domain.Entities;

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
            return new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, 5, workers);
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
            return new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, databases, 3, 5, 5);
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
            return new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, sftpUsers, 5, 5);
        });
    }

    /// <summary>A plan with a positive worker budget is created.</summary>
    [Fact]
    public void A_plan_with_a_positive_worker_budget_is_created()
    {
        var plan = new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, 5, 7);

        Assert.Equal(7, plan.MaxPhpWorkersPerPool);
    }

    /// <summary>A plan with a positive database allowance is created.</summary>
    [Fact]
    public void A_plan_with_a_positive_database_allowance_is_created()
    {
        // Guards the refusal above from passing because the constructor throws for some other
        // reason: a guard that rejects everything is not a guard.
        var plan = new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 4, 3, 5, 7);

        Assert.Equal(4, plan.MaxDatabases);
    }

    /// <summary>A plan with a positive sftp allowance is created.</summary>
    [Fact]
    public void A_plan_with_a_positive_sftp_allowance_is_created()
    {
        // Guards the refusal above from passing because the constructor throws for some other
        // reason: a guard that rejects everything is not a guard.
        var plan = new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 4, 6, 5, 7);

        Assert.Equal(6, plan.MaxSftpUsers);
    }

    /// <summary>A plan with a negative cron allowance cannot be created.</summary>
    [Fact]
    public void A_plan_with_a_negative_cron_allowance_cannot_be_created()
    {
        // Negative is not a smaller allowance, it is nonsense — and it would compare as "under the
        // limit" against a crontab that already holds entries, which is the direction that lets one
        // through rather than the direction that refuses.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, -1, 5);
        });
    }

    /// <summary>A plan with a zero cron allowance is created because a tier may include no scheduled tasks.</summary>
    [Fact]
    public void A_plan_with_a_zero_cron_allowance_is_created_because_a_tier_may_include_no_scheduled_tasks()
    {
        // The deliberate difference from the database and SFTP allowances above, which refuse zero.
        // An account with no SFTP login cannot fill its own sites and a pool with no workers cannot
        // serve PHP, so zero there is a broken plan; "this tier has no scheduled tasks" is a product
        // a hosting company may genuinely sell, and refusing it here would forbid selling it.
        var plan = new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, 0, 5);

        Assert.Equal(0, plan.MaxCronEntries);
    }

    /// <summary>A plan carries the cron allowance it was created with.</summary>
    [Fact]
    public void A_plan_carries_the_cron_allowance_it_was_created_with()
    {
        // The value has to survive the constructor to the property, and it has to be THIS value:
        // every other integer on a plan is also a small number, so a mis-ordered argument list is
        // invisible without an assertion that names one.
        var plan = new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 4, 6, 9, 7);

        Assert.Equal(9, plan.MaxCronEntries);
    }
}
