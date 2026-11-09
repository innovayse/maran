using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sites.Tests.Domain.Policies;

/// <summary>Behavioral contract of <see cref="PhpSettingCatalogue"/>.</summary>
public sealed class PhpSettingCatalogueTests
{
    /// <summary>The bounds the plan states in these tests.</summary>
    private static readonly PhpLimitsSnapshot Limits = new(256, 64, 120, 3000);

    /// <summary>Every name of the catalogue is known and has a kind.</summary>
    [Fact]
    public void Every_name_of_the_catalogue_is_known_and_has_a_kind()
    {
        foreach (var name in PhpSettingNames.All)
        {
            Assert.True(PhpSettingCatalogue.IsKnown(name));
            _ = PhpSettingCatalogue.KindOf(name);
        }
    }

    /// <summary>A name that is not on the list, however close, is not known.</summary>
    [Theory]
    [InlineData("open_basedir")]
    [InlineData("disable_functions")]
    [InlineData("allow_url_fopen")]
    [InlineData("cgi.fix_pathinfo")]
    [InlineData("Memory_Limit")]
    [InlineData("memory_limit ")]
    [InlineData("")]
    public void A_name_that_is_not_on_the_list_is_not_known(string name)
    {
        Assert.False(PhpSettingCatalogue.IsKnown(name));
        Assert.Throws<ArgumentOutOfRangeException>(() => { return PhpSettingCatalogue.KindOf(name); });
    }

    /// <summary>The three sizes are sizes, the others have their own kind.</summary>
    [Fact]
    public void The_three_sizes_are_sizes_and_the_others_have_their_own_kind()
    {
        Assert.Equal(PhpSettingKind.Size, PhpSettingCatalogue.KindOf(PhpSettingNames.MemoryLimit));
        Assert.Equal(PhpSettingKind.Size, PhpSettingCatalogue.KindOf(PhpSettingNames.UploadMaxFilesize));
        Assert.Equal(PhpSettingKind.Size, PhpSettingCatalogue.KindOf(PhpSettingNames.PostMaxSize));
        Assert.Equal(PhpSettingKind.Seconds, PhpSettingCatalogue.KindOf(PhpSettingNames.MaxExecutionTime));
        Assert.Equal(PhpSettingKind.Count, PhpSettingCatalogue.KindOf(PhpSettingNames.MaxInputVars));
        Assert.Equal(PhpSettingKind.Timezone, PhpSettingCatalogue.KindOf(PhpSettingNames.DateTimezone));
    }

    /// <summary>One upload bound governs both the upload size and the request size.</summary>
    [Fact]
    public void One_upload_bound_governs_both_the_upload_size_and_the_request_size()
    {
        Assert.Equal(64, PhpSettingCatalogue.PlanMaximum(PhpSettingNames.UploadMaxFilesize, Limits));
        Assert.Equal(64, PhpSettingCatalogue.PlanMaximum(PhpSettingNames.PostMaxSize, Limits));
        Assert.Equal(256, PhpSettingCatalogue.PlanMaximum(PhpSettingNames.MemoryLimit, Limits));
        Assert.Equal(120, PhpSettingCatalogue.PlanMaximum(PhpSettingNames.MaxExecutionTime, Limits));
        Assert.Equal(3000, PhpSettingCatalogue.PlanMaximum(PhpSettingNames.MaxInputVars, Limits));
    }

    /// <summary>The timezone has no bound and no absolute maximum.</summary>
    [Fact]
    public void The_timezone_has_no_bound_and_no_absolute_maximum()
    {
        Assert.Null(PhpSettingCatalogue.PlanMaximum(PhpSettingNames.DateTimezone, Limits));
        Assert.Null(PhpSettingCatalogue.AbsoluteMaximum(PhpSettingNames.DateTimezone));
    }

    /// <summary>The absolute maxima are the agent's ceilings.</summary>
    [Fact]
    public void The_absolute_maxima_are_the_agents_ceilings()
    {
        Assert.Equal(PhpSettingCeilings.MaxSizeMb, PhpSettingCatalogue.AbsoluteMaximum(PhpSettingNames.MemoryLimit));
        Assert.Equal(PhpSettingCeilings.MaxExecutionSeconds, PhpSettingCatalogue.AbsoluteMaximum(PhpSettingNames.MaxExecutionTime));
        Assert.Equal(PhpSettingCeilings.MaxInputVars, PhpSettingCatalogue.AbsoluteMaximum(PhpSettingNames.MaxInputVars));
    }
}
