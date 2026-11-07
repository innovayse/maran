using Maran.Modules.Identity.Domain.Entities;

namespace Maran.Modules.Identity.Tests.Domain;

/// <summary>Behavioural contract of the audit retention policy and its bounds.</summary>
public sealed class AuditRetentionPolicyTests
{
    /// <summary>When the policy was created.</summary>
    private static readonly DateTimeOffset Created = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    /// <summary>When the policy was replaced.</summary>
    private static readonly DateTimeOffset Replaced = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    /// <summary>A fresh panel keeps the journal indefinitely.</summary>
    [Fact]
    public void The_default_keeps_the_journal_indefinitely()
    {
        Assert.Null(AuditRetentionPolicy.DefaultRetentionDays);
        Assert.Null(new AuditRetentionPolicy(AuditRetentionPolicy.DefaultRetentionDays, Created).RetentionDays);
    }

    /// <summary>The policy is the singleton row and has not run yet.</summary>
    [Fact]
    public void A_new_policy_is_the_singleton_and_has_not_run()
    {
        var policy = new AuditRetentionPolicy(90, Created);

        Assert.Equal(AuditRetentionPolicy.SingletonId, policy.Id);
        Assert.Equal(90, policy.RetentionDays);
        Assert.Equal(Created, policy.UpdatedAt);
        Assert.Null(policy.LastRunAt);
    }

    /// <summary>The bounds are inclusive at both ends and null is allowed.</summary>
    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(3649)]
    [InlineData(3650)]
    public void A_period_inside_the_bounds_is_accepted(int days)
    {
        var policy = new AuditRetentionPolicy(365, Created);

        policy.Replace(days, Replaced);

        Assert.Equal(days, policy.RetentionDays);
        Assert.Equal(Replaced, policy.UpdatedAt);
        Assert.True(AuditRetentionPolicy.IsPermitted(days));
    }

    /// <summary>Replacing with null goes back to keeping the journal indefinitely.</summary>
    [Fact]
    public void Replacing_with_null_keeps_the_journal_indefinitely()
    {
        var policy = new AuditRetentionPolicy(365, Created);

        policy.Replace(null, Replaced);

        Assert.Null(policy.RetentionDays);
        Assert.True(AuditRetentionPolicy.IsPermitted(null));
    }

    /// <summary>A period outside the bounds is refused by Replace and leaves the policy as it was.</summary>
    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(29)]
    [InlineData(3651)]
    [InlineData(int.MaxValue)]
    public void A_period_outside_the_bounds_is_refused(int days)
    {
        var policy = new AuditRetentionPolicy(365, Created);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            policy.Replace(days, Replaced);
        });

        Assert.Equal(365, policy.RetentionDays);
        Assert.Equal(Created, policy.UpdatedAt);
        Assert.False(AuditRetentionPolicy.IsPermitted(days));
    }

    /// <summary>The constructor enforces the same bounds as Replace.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(3651)]
    public void The_constructor_refuses_a_period_outside_the_bounds(int days)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return new AuditRetentionPolicy(days, Created);
        });
    }

    /// <summary>Recording a run stores when it happened.</summary>
    [Fact]
    public void Recording_a_run_stores_its_time()
    {
        var policy = new AuditRetentionPolicy(365, Created);

        policy.RecordRun(Replaced);

        Assert.Equal(Replaced, policy.LastRunAt);
    }
}
