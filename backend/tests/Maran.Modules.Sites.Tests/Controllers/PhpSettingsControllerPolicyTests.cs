using Maran.Modules.Sites.Controllers;
using Maran.Sdk.Contracts;
using Maran.Sdk.Filters;

namespace Maran.Modules.Sites.Tests.Controllers;

/// <summary>Pins how <see cref="PhpSettingsController"/> gates by feature and what a view-as session may do.</summary>
public sealed class PhpSettingsControllerPolicyTests
{
    /// <summary>The controller requires the Php feature.</summary>
    [Fact]
    public void The_controller_requires_the_php_feature()
    {
        var feature = typeof(PhpSettingsController)
            .GetCustomAttributes(typeof(RequiresAccountFeatureAttribute), inherit: true)
            .Cast<RequiresAccountFeatureAttribute>()
            .Single();

        Assert.Equal(AccountFeature.Php, feature.Feature);
    }

    /// <summary>Reading is a view-as read and saving is refused to a view-as session.</summary>
    [Fact]
    public void Reading_is_a_view_as_read_and_saving_is_refused()
    {
        var read = typeof(PhpSettingsController).GetMethod(nameof(PhpSettingsController.GetAsync))!
            .GetCustomAttributes(typeof(ViewAsAccessAttribute), inherit: true).Cast<ViewAsAccessAttribute>().Single();
        var write = typeof(PhpSettingsController).GetMethod(nameof(PhpSettingsController.ReplaceAsync))!
            .GetCustomAttributes(typeof(ViewAsAccessAttribute), inherit: true).Cast<ViewAsAccessAttribute>().Single();

        Assert.Equal(ViewAsAccess.Read, read.Access);
        Assert.Equal(ViewAsAccess.Refused, write.Access);
    }
}
