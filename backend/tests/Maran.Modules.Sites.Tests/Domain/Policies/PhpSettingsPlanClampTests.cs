using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sites.Tests.Domain.Policies;

/// <summary>Behavioral contract of <see cref="PhpSettingsPlanClamp"/>.</summary>
public sealed class PhpSettingsPlanClampTests
{
    /// <summary>The bounds the plan states in these tests.</summary>
    private static readonly PhpLimitsSnapshot Limits = new(256, 64, 120, 3000);

    /// <summary>A value inside the plan passes through unchanged.</summary>
    [Fact]
    public void A_value_inside_the_plan_passes_through_unchanged()
    {
        var effective = PhpSettingsPlanClamp.Apply([Row("memory_limit", "128M")], Limits);

        var setting = Assert.Single(effective);
        Assert.Equal("128M", setting.Value);
    }

    /// <summary>A value above the plan is cut to the bound and never raised.</summary>
    [Fact]
    public void A_value_above_the_plan_is_cut_to_the_bound()
    {
        var effective = PhpSettingsPlanClamp.Apply(
            [Row("memory_limit", "512M"), Row("max_execution_time", "300"), Row("max_input_vars", "10000")],
            Limits);

        Assert.Equal(["256M", "120", "3000"], effective.Select(setting => { return setting.Value; }).ToList());
    }

    /// <summary>The request size is cut to the upload bound, so it stays at least the upload size.</summary>
    [Fact]
    public void The_request_size_is_cut_to_the_upload_bound_and_stays_at_least_the_upload_size()
    {
        var effective = PhpSettingsPlanClamp.Apply(
            [Row("upload_max_filesize", "100M"), Row("post_max_size", "200M")],
            Limits);

        Assert.Equal(
            [("upload_max_filesize", "64M"), ("post_max_size", "64M")],
            effective.Select(setting => { return (setting.Name, setting.Value); }).ToList());
    }

    /// <summary>The timezone has no bound and passes through.</summary>
    [Fact]
    public void The_timezone_passes_through()
    {
        var effective = PhpSettingsPlanClamp.Apply([Row("date.timezone", "Europe/Berlin")], Limits);

        Assert.Equal("Europe/Berlin", Assert.Single(effective).Value);
    }

    /// <summary>A numeric value that cannot be read is dropped rather than sent to the agent.</summary>
    [Fact]
    public void A_numeric_value_that_cannot_be_read_is_dropped()
    {
        var effective = PhpSettingsPlanClamp.Apply([Row("memory_limit", "lots"), Row("max_input_vars", "1000")], Limits);

        Assert.Equal("max_input_vars", Assert.Single(effective).Name);
    }

    /// <summary>A name that is not on the list is ignored, never passed on.</summary>
    [Fact]
    public void A_name_that_is_not_on_the_list_is_ignored()
    {
        var effective = PhpSettingsPlanClamp.Apply([Row("disable_functions", "none")], Limits);

        Assert.Empty(effective);
    }

    /// <summary>The answer follows the catalogue's order whatever order the rows come in.</summary>
    [Fact]
    public void The_answer_follows_the_catalogues_order()
    {
        var effective = PhpSettingsPlanClamp.Apply(
            [Row("date.timezone", "UTC"), Row("memory_limit", "64M")],
            Limits);

        Assert.Equal(["memory_limit", "date.timezone"], effective.Select(setting => { return setting.Name; }).ToList());
    }

    /// <summary>Builds a stored setting for an arbitrary account.</summary>
    /// <param name="name">The setting's name.</param>
    /// <param name="value">The stored value.</param>
    private static PhpSetting Row(string name, string value)
    {
        return new PhpSetting(Guid.NewGuid(), Guid.NewGuid(), name, value);
    }
}
