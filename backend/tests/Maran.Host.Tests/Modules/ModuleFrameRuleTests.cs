using Maran.Host.Modules;
using Maran.Host.Tests.Modules.Fixtures;
using Maran.Sdk.Contracts;

namespace Maran.Host.Tests.Modules;

/// <summary>
/// Behavioral contract of <see cref="ModuleFrameRule"/>: a module is framed only when it is packaged,
/// active and its bundle belongs to the running version, and its frame lives at an address the panel builds.
/// </summary>
public sealed class ModuleFrameRuleTests
{
    /// <summary>The manifest of the module under test.</summary>
    private static readonly Manifest Running = TestManifests.Of("hello", "1.2.0");

    /// <summary>A bundle of the running version.</summary>
    private static readonly ModuleSpaDescription Bundle = new("1.2.0", "entry.js", "sha384-x", ["en"]);

    /// <summary>A packaged, active module with a bundle of its running version is framed.</summary>
    [Fact]
    public void A_packaged_active_module_with_a_bundle_of_its_running_version_is_framed()
    {
        Assert.Same(Bundle, ModuleFrameRule.Frame(Running, true, ModuleLifecycleState.Active, Bundle));
    }

    /// <summary>Degraded, failed, installed and removed modules are not framed.</summary>
    /// <param name="state">A state other than active.</param>
    [Theory]
    [InlineData(ModuleLifecycleState.Degraded)]
    [InlineData(ModuleLifecycleState.Failed)]
    [InlineData(ModuleLifecycleState.Installed)]
    [InlineData(ModuleLifecycleState.Removed)]
    public void A_module_that_is_not_active_is_not_framed(ModuleLifecycleState state)
    {
        Assert.Null(ModuleFrameRule.Frame(Running, true, state, Bundle));
    }

    /// <summary>A module that shipped with the panel is not framed.</summary>
    [Fact]
    public void A_module_that_shipped_with_the_panel_is_not_framed()
    {
        Assert.Null(ModuleFrameRule.Frame(Running, false, ModuleLifecycleState.Active, Bundle));
    }

    /// <summary>A bundle of another version than the running one is not framed.</summary>
    [Fact]
    public void A_bundle_of_another_version_is_not_framed()
    {
        var other = Bundle with { Version = "1.3.0" };

        Assert.Null(ModuleFrameRule.Frame(Running, true, ModuleLifecycleState.Active, other));
    }

    /// <summary>A module the window holds no bundle for is not framed.</summary>
    [Fact]
    public void A_module_with_no_bundle_is_not_framed()
    {
        Assert.Null(ModuleFrameRule.Frame(Running, true, ModuleLifecycleState.Active, null));
    }

    /// <summary>The frame address is built from the id and version with a trailing slash.</summary>
    [Fact]
    public void The_frame_address_is_built_from_the_id_and_version()
    {
        Assert.Equal("/module-frames/hello/1.2.0/", ModuleFrameRule.FrameUrlOf("hello", "1.2.0"));
    }
}
