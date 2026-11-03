using Maran.Modules.Accounts.Domain.Policies;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Tests.Domain.Policies;

/// <summary>The arithmetic of <see cref="ResellerEnvelopePolicy"/>: every comparison is inclusive.</summary>
public sealed class ResellerEnvelopePolicyTests
{
    /// <summary>The three figures a plan over three ceilings is named for, in the order the policy lists them.</summary>
    private static readonly string[] ThreeViolations = { "DiskQuotaMb", "MaxCronEntries", "Features" };

    /// <summary>A plan equal to every ceiling at once is accepted: the limits are inclusive.</summary>
    [Fact]
    public void A_plan_equal_to_every_ceiling_is_accepted()
    {
        var violations = ResellerEnvelopePolicy.Violations(Envelope(), AtTheCeilings());

        Assert.Empty(violations);
    }

    /// <summary>A plan one over a single numeric ceiling is refused, naming exactly that figure.</summary>
    /// <param name="field">The figure raised by one.</param>
    [Theory]
    [InlineData("DiskQuotaMb")]
    [InlineData("MaxSites")]
    [InlineData("MaxDatabases")]
    [InlineData("MaxSftpUsers")]
    [InlineData("MaxFtpUsers")]
    [InlineData("MaxCronEntries")]
    [InlineData("MaxPhpWorkersPerPool")]
    public void A_plan_one_over_a_ceiling_is_refused_naming_that_figure(string field)
    {
        var atCeilings = AtTheCeilings();
        var over = field switch
        {
            "DiskQuotaMb" => atCeilings with { DiskQuotaMb = 101 },
            "MaxSites" => atCeilings with { MaxSites = 11 },
            "MaxDatabases" => atCeilings with { MaxDatabases = 12 },
            "MaxSftpUsers" => atCeilings with { MaxSftpUsers = 13 },
            "MaxFtpUsers" => atCeilings with { MaxFtpUsers = 14 },
            "MaxCronEntries" => atCeilings with { MaxCronEntries = 15 },
            "MaxPhpWorkersPerPool" => atCeilings with { MaxPhpWorkersPerPool = 16 },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        var violations = ResellerEnvelopePolicy.Violations(Envelope(), over);

        Assert.Equal(new[] { field }, violations);
    }

    /// <summary>A plan holding a feature outside the envelope's set is refused, naming Features.</summary>
    [Fact]
    public void A_feature_outside_the_envelope_is_refused()
    {
        var limits = AtTheCeilings() with { Features = AccountFeature.Mail | AccountFeature.Backups };

        var violations = ResellerEnvelopePolicy.Violations(Envelope(), limits);

        Assert.Equal("Features", Assert.Single(violations));
    }

    /// <summary>A plan holding a strict subset of the envelope's features, or none, is accepted.</summary>
    /// <param name="features">The plan's feature set.</param>
    [Theory]
    [InlineData(AccountFeature.None)]
    [InlineData(AccountFeature.Mail)]
    [InlineData(AccountFeature.Dns)]
    public void A_subset_of_the_envelope_features_is_accepted(AccountFeature features)
    {
        var limits = AtTheCeilings() with { Features = features };

        Assert.Empty(ResellerEnvelopePolicy.Violations(Envelope(), limits));
    }

    /// <summary>An envelope with no features accepts only a plan with none.</summary>
    [Fact]
    public void An_envelope_without_features_refuses_any_feature()
    {
        var envelope = ResellerTestData.Envelope(features: AccountFeature.None);
        var limits = ResellerTestData.Limits(features: AccountFeature.Sites);

        Assert.Equal("Features", Assert.Single(ResellerEnvelopePolicy.Violations(envelope, limits)));
    }

    /// <summary>An unlimited envelope ceiling is no ceiling: any plan figure, even none, passes.</summary>
    [Fact]
    public void An_unlimited_runtime_ceiling_accepts_any_plan_figure()
    {
        var envelope = ResellerTestData.Envelope();
        var limits = ResellerTestData.Limits(
            runtimeLimits: RuntimeLimits.Of(
                ResourceLimit.Limited(100_000),
                ResourceLimit.Unlimited(),
                ResourceLimit.Limited(1)));

        Assert.Empty(ResellerEnvelopePolicy.Violations(envelope, limits));
    }

    /// <summary>A runtime figure equal to the ceiling is accepted, one over is refused naming that ceiling.</summary>
    /// <param name="ceiling">Which runtime ceiling is exercised.</param>
    [Theory]
    [InlineData("CpuQuotaPercent")]
    [InlineData("MemoryMaxMb")]
    [InlineData("MaxProcesses")]
    public void A_runtime_figure_is_judged_inclusively(string ceiling)
    {
        var envelope = ResellerTestData.Envelope(
            runtimeLimits: RuntimeLimits.Of(
                ResourceLimit.Limited(200),
                ResourceLimit.Limited(2_048),
                ResourceLimit.Limited(300)));

        var atLimit = ResellerTestData.Limits(runtimeLimits: RuntimeFor(ceiling, 0));
        var overLimit = ResellerTestData.Limits(runtimeLimits: RuntimeFor(ceiling, 1));

        Assert.Empty(ResellerEnvelopePolicy.Violations(envelope, atLimit));
        Assert.Equal(new[] { ceiling }, ResellerEnvelopePolicy.Violations(envelope, overLimit));
    }

    /// <summary>A plan stating no cap is unbounded, which is over any ceiling that exists.</summary>
    [Fact]
    public void A_plan_with_no_cap_is_over_a_limited_ceiling()
    {
        var envelope = ResellerTestData.Envelope(
            runtimeLimits: RuntimeLimits.Of(
                ResourceLimit.Limited(200),
                ResourceLimit.Unlimited(),
                ResourceLimit.Unlimited()));
        var limits = ResellerTestData.Limits(runtimeLimits: RuntimeLimits.Unlimited());

        Assert.Equal("CpuQuotaPercent", Assert.Single(ResellerEnvelopePolicy.Violations(envelope, limits)));
    }

    /// <summary>Several breaches are all named, in the stable order the policy checks them.</summary>
    [Fact]
    public void Several_breaches_are_named_in_a_stable_order()
    {
        var limits = AtTheCeilings() with
        {
            DiskQuotaMb = 101,
            MaxCronEntries = 15,
            Features = AccountFeature.Backups,
        };

        var violations = ResellerEnvelopePolicy.Violations(Envelope(), limits);

        Assert.Equal(ThreeViolations, violations);
    }

    /// <summary>One more plan is allowed while the count is below the maximum, refused at it.</summary>
    [Fact]
    public void A_plan_is_allowed_only_while_the_count_is_below_the_maximum()
    {
        var envelope = Envelope();

        Assert.True(ResellerEnvelopePolicy.PlanAllowed(envelope, 0));
        Assert.True(ResellerEnvelopePolicy.PlanAllowed(envelope, 2));
        Assert.False(ResellerEnvelopePolicy.PlanAllowed(envelope, 3));
        Assert.False(ResellerEnvelopePolicy.PlanAllowed(envelope, 4));
    }

    /// <summary>One more account is allowed while the count is below the maximum, refused at it.</summary>
    [Fact]
    public void An_account_is_allowed_only_while_the_count_is_below_the_maximum()
    {
        var envelope = Envelope();

        Assert.True(ResellerEnvelopePolicy.AccountAllowed(envelope, 0));
        Assert.True(ResellerEnvelopePolicy.AccountAllowed(envelope, 3));
        Assert.False(ResellerEnvelopePolicy.AccountAllowed(envelope, 4));
        Assert.False(ResellerEnvelopePolicy.AccountAllowed(envelope, 5));
    }

    /// <summary>An envelope selling zero accounts or zero plans allows none.</summary>
    [Fact]
    public void An_envelope_of_zero_allows_no_account_and_no_plan()
    {
        var envelope = ResellerTestData.Envelope(maxAccounts: 0, maxPlans: 0);

        Assert.False(ResellerEnvelopePolicy.AccountAllowed(envelope, 0));
        Assert.False(ResellerEnvelopePolicy.PlanAllowed(envelope, 0));
    }

    /// <summary>A disk allocation equal to the pool is accepted, one megabyte over is refused.</summary>
    [Fact]
    public void A_disk_allocation_equal_to_the_pool_is_accepted_and_one_over_is_refused()
    {
        var envelope = Envelope();

        Assert.True(ResellerEnvelopePolicy.DiskPoolAllowed(envelope, 0));
        Assert.True(ResellerEnvelopePolicy.DiskPoolAllowed(envelope, 999));
        Assert.True(ResellerEnvelopePolicy.DiskPoolAllowed(envelope, 1_000));
        Assert.False(ResellerEnvelopePolicy.DiskPoolAllowed(envelope, 1_001));
    }

    /// <summary>Builds runtime ceilings with one ceiling <paramref name="extra"/> over the envelope's.</summary>
    /// <param name="ceiling">Which ceiling carries the extra.</param>
    /// <param name="extra">How far over the envelope's figure to go; zero is exactly on it.</param>
    /// <returns>The runtime limits of the plan.</returns>
    private static RuntimeLimits RuntimeFor(string ceiling, int extra)
    {
        var cpu = ResourceLimit.Limited(200 + (ceiling == "CpuQuotaPercent" ? extra : 0));
        var memory = ResourceLimit.Limited(2_048 + (ceiling == "MemoryMaxMb" ? extra : 0));
        var processes = ResourceLimit.Limited(300 + (ceiling == "MaxProcesses" ? extra : 0));

        return RuntimeLimits.Of(cpu, memory, processes);
    }

    /// <summary>An envelope whose every ceiling is a distinct figure, so a swapped field is noticed.</summary>
    /// <returns>The envelope.</returns>
    private static ResellerEnvelope Envelope()
    {
        return ResellerTestData.Envelope(
            maxAccounts: 4,
            diskPoolMb: 1_000,
            maxPlans: 3,
            maxDiskQuotaMb: 100,
            maxSites: 10,
            maxDatabases: 11,
            maxSftpUsers: 12,
            maxFtpUsers: 13,
            maxCronEntries: 14,
            maxPhpWorkersPerPool: 15,
            features: AccountFeature.Mail | AccountFeature.Dns);
    }

    /// <summary>Limits sitting exactly on every ceiling of <see cref="Envelope"/>.</summary>
    /// <returns>The limits.</returns>
    private static PlanLimits AtTheCeilings()
    {
        return ResellerTestData.Limits(
            diskQuotaMb: 100,
            maxSites: 10,
            maxDatabases: 11,
            maxSftpUsers: 12,
            maxFtpUsers: 13,
            maxCronEntries: 14,
            maxPhpWorkersPerPool: 15,
            features: AccountFeature.Mail | AccountFeature.Dns);
    }
}
