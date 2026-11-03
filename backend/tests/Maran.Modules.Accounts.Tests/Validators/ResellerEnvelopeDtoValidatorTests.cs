using FluentValidation.Results;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Modules.Accounts.Validators;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Tests.Validators;

/// <summary>
/// Behavioral contract of <see cref="ResellerEnvelopeDtoValidator"/>: every figure is held to its
/// bounds, the feature list and the three ceilings are required, and a valid envelope is accepted.
/// </summary>
public sealed class ResellerEnvelopeDtoValidatorTests
{
    /// <summary>The figure bound the validator states, restated so a change to it is a visible test edit.</summary>
    private const int MaxFigure = 1_073_741_824;

    /// <summary>The validator under test.</summary>
    private readonly ResellerEnvelopeDtoValidator _validator = new();

    /// <summary>A valid envelope, with every figure inside its bounds, is accepted: the inverse control.</summary>
    [Fact]
    public void A_valid_envelope_is_accepted()
    {
        Assert.True(_validator.Validate(ResellerEnvelopeDtoTestData.Valid()).IsValid);
    }

    /// <summary>Zero for every count is accepted, because an envelope that sells nothing is a real product.</summary>
    [Fact]
    public void Zero_for_every_count_is_accepted()
    {
        var envelope = ResellerEnvelopeDtoTestData.Valid() with
        {
            MaxAccounts = 0,
            DiskPoolMb = 0,
            MaxPlans = 0,
            MaxDiskQuotaMb = 0,
            MaxSites = 0,
            MaxDatabases = 0,
            MaxSftpUsers = 0,
            MaxFtpUsers = 0,
            MaxCronEntries = 0,
            MaxPhpWorkersPerPool = 0,
            Features = [],
        };

        Assert.True(_validator.Validate(envelope).IsValid);
    }

    /// <summary>The upper bound of every figure is accepted and one above it is refused.</summary>
    [Fact]
    public void Every_figure_accepts_its_upper_bound_and_refuses_one_above_it()
    {
        var valid = ResellerEnvelopeDtoTestData.Valid();

        foreach (var (name, set) in Figures())
        {
            Assert.True(_validator.Validate(set(valid, MaxFigure)).IsValid, $"{name} at the bound must be accepted");
            AssertRefusedOn(_validator.Validate(set(valid, MaxFigure + 1L)), name);
        }
    }

    /// <summary>A negative figure is refused with the envelope code, for every figure.</summary>
    [Fact]
    public void A_negative_figure_is_refused_with_the_envelope_code_for_every_figure()
    {
        var valid = ResellerEnvelopeDtoTestData.Valid();
        var negatives = new (string Name, ResellerEnvelopeDto Envelope)[]
        {
            (nameof(ResellerEnvelopeDto.MaxAccounts), valid with { MaxAccounts = -1 }),
            (nameof(ResellerEnvelopeDto.DiskPoolMb), valid with { DiskPoolMb = -1 }),
            (nameof(ResellerEnvelopeDto.MaxPlans), valid with { MaxPlans = -1 }),
            (nameof(ResellerEnvelopeDto.MaxDiskQuotaMb), valid with { MaxDiskQuotaMb = -1 }),
            (nameof(ResellerEnvelopeDto.MaxSites), valid with { MaxSites = -1 }),
            (nameof(ResellerEnvelopeDto.MaxDatabases), valid with { MaxDatabases = -1 }),
            (nameof(ResellerEnvelopeDto.MaxSftpUsers), valid with { MaxSftpUsers = -1 }),
            (nameof(ResellerEnvelopeDto.MaxFtpUsers), valid with { MaxFtpUsers = -1 }),
            (nameof(ResellerEnvelopeDto.MaxCronEntries), valid with { MaxCronEntries = -1 }),
            (nameof(ResellerEnvelopeDto.MaxPhpWorkersPerPool), valid with { MaxPhpWorkersPerPool = -1 }),
        };

        foreach (var (name, envelope) in negatives)
        {
            AssertRefusedOn(_validator.Validate(envelope), name);
        }
    }

    /// <summary>A feature list left out is refused with the unknown-feature code.</summary>
    [Fact]
    public void A_feature_list_left_out_is_refused()
    {
        var result = _validator.Validate(ResellerEnvelopeDtoTestData.Valid() with { Features = null! });

        var failure = Assert.Single(result.Errors);
        Assert.Equal("PlanFeatureUnknown", failure.ErrorMessage);
    }

    /// <summary>A member that is a combination of features, or none, is refused as an unknown feature.</summary>
    [Fact]
    public void A_feature_member_that_names_no_single_feature_is_refused()
    {
        var combined = _validator.Validate(
            ResellerEnvelopeDtoTestData.Valid() with { Features = [AccountFeature.Mail | AccountFeature.Dns] });
        var none = _validator.Validate(ResellerEnvelopeDtoTestData.Valid() with { Features = [AccountFeature.None] });

        Assert.Equal("PlanFeatureUnknown", Assert.Single(combined.Errors).ErrorMessage);
        Assert.Equal("PlanFeatureUnknown", Assert.Single(none.Errors).ErrorMessage);
    }

    /// <summary>A ceiling left out is refused, never read as unlimited, for each of the three ceilings.</summary>
    [Fact]
    public void A_ceiling_left_out_is_refused_never_read_as_unlimited()
    {
        var valid = ResellerEnvelopeDtoTestData.Valid();

        AssertRefusedOn(_validator.Validate(valid with { CpuQuotaPercent = null! }), "CpuQuotaPercent");
        AssertRefusedOn(_validator.Validate(valid with { MemoryMaxMb = null! }), "MemoryMaxMb");
        AssertRefusedOn(_validator.Validate(valid with { MaxProcesses = null! }), "MaxProcesses");
    }

    /// <summary>A limited ceiling must be between one and the figure bound; an unlimited one needs no figure.</summary>
    [Fact]
    public void A_limited_ceiling_is_held_to_one_through_the_bound_and_an_unlimited_one_is_accepted()
    {
        var valid = ResellerEnvelopeDtoTestData.Valid();

        Assert.True(_validator.Validate(valid with { MaxProcesses = ResourceCeilingTestValues.Unlimited }).IsValid);
        Assert.True(_validator.Validate(valid with { MaxProcesses = ResourceCeilingTestValues.Limited(1) }).IsValid);
        Assert.True(
            _validator.Validate(valid with { MaxProcesses = ResourceCeilingTestValues.Limited(MaxFigure) }).IsValid);
        AssertRefusedOn(
            _validator.Validate(valid with { MaxProcesses = ResourceCeilingTestValues.Limited(0) }),
            "MaxProcesses");
        AssertRefusedOn(
            _validator.Validate(valid with { MemoryMaxMb = ResourceCeilingTestValues.Limited(MaxFigure + 1) }),
            "MemoryMaxMb");
        AssertRefusedOn(
            _validator.Validate(valid with { CpuQuotaPercent = ResourceCeilingTestValues.Limited(-5) }),
            "CpuQuotaPercent");
    }

    /// <summary>Asserts that the result holds a failure on <paramref name="property"/>.</summary>
    /// <param name="result">The validation result.</param>
    /// <param name="property">The figure the failure must name.</param>
    private static void AssertRefusedOn(ValidationResult result, string property)
    {
        Assert.Contains(
            result.Errors,
            failure =>
            {
                return failure.PropertyName.StartsWith(property, StringComparison.Ordinal);
            });
    }

    /// <summary>Each bounded figure by name, with the way to set it on an envelope.</summary>
    private static IEnumerable<(string Name, Func<ResellerEnvelopeDto, long, ResellerEnvelopeDto> Set)> Figures()
    {
        yield return (nameof(ResellerEnvelopeDto.MaxAccounts), (e, v) => { return e with { MaxAccounts = (int)v }; });
        yield return (nameof(ResellerEnvelopeDto.DiskPoolMb), (e, v) => { return e with { DiskPoolMb = v }; });
        yield return (nameof(ResellerEnvelopeDto.MaxPlans), (e, v) => { return e with { MaxPlans = (int)v }; });
        yield return (
            nameof(ResellerEnvelopeDto.MaxDiskQuotaMb),
            (e, v) => { return e with { MaxDiskQuotaMb = (int)v }; });
        yield return (nameof(ResellerEnvelopeDto.MaxSites), (e, v) => { return e with { MaxSites = (int)v }; });
        yield return (nameof(ResellerEnvelopeDto.MaxDatabases), (e, v) => { return e with { MaxDatabases = (int)v }; });
        yield return (nameof(ResellerEnvelopeDto.MaxSftpUsers), (e, v) => { return e with { MaxSftpUsers = (int)v }; });
        yield return (nameof(ResellerEnvelopeDto.MaxFtpUsers), (e, v) => { return e with { MaxFtpUsers = (int)v }; });
        yield return (
            nameof(ResellerEnvelopeDto.MaxCronEntries),
            (e, v) => { return e with { MaxCronEntries = (int)v }; });
        yield return (
            nameof(ResellerEnvelopeDto.MaxPhpWorkersPerPool),
            (e, v) => { return e with { MaxPhpWorkersPerPool = (int)v }; });
    }
}
