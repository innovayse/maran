using System.Security.Cryptography;
using Maran.Packaging.Models;
using Maran.Packaging.Services;
using Maran.Packaging.Tests.Builders;
using Maran.Sdk.Contracts;

namespace Maran.Packaging.Tests.Services;

/// <summary>
/// Whether a package fits the panel and the installation. Pure functions: the panel, the clock and the
/// installed modules are all arguments, and every refusal has an accepting case beside it.
/// </summary>
public sealed class IpmCompatibilityTests : IDisposable
{
    /// <summary>The throwaway key behind the fixture builder.</summary>
    private readonly ECDsa _key = IpmFixtureBuilder.NewKey();

    /// <summary>Frees the throwaway key.</summary>
    public void Dispose()
    {
        _key.Dispose();
    }

    /// <summary>Only the revision this library reads is accepted.</summary>
    [Fact]
    public void Only_the_known_format_revision_is_accepted()
    {
        Assert.True(IpmCompatibility.CheckFormat(1).IsSuccess);
        RefusalAssert.Is("IpmFormatVersionUnknown", IpmCompatibility.CheckFormat(0).Error);
        RefusalAssert.Is("IpmFormatVersionUnknown", IpmCompatibility.CheckFormat(2).Error);
        RefusalAssert.Is("IpmFormatVersionUnknown", IpmCompatibility.CheckFormat(-1).Error);
    }

    /// <summary>A panel above the ceiling is refused, and a panel at the ceiling or a package with no ceiling is accepted.</summary>
    [Fact]
    public void A_panel_newer_than_the_ceiling_is_refused_and_one_at_the_ceiling_is_accepted()
    {
        var manifest = Good();

        Assert.True(IpmCompatibility.CheckPanel(manifest, TestPanel.Version, TestPanel.Now).IsSuccess);
        Assert.True(IpmCompatibility.CheckPanel(manifest with { MaxPanelVersion = "1.4.0" }, TestPanel.Version, TestPanel.Now).IsSuccess);
        RefusalAssert.Is("IpmPanelTooNew", IpmCompatibility.CheckPanel(manifest with { MaxPanelVersion = "1.3.9" }, TestPanel.Version, TestPanel.Now).Error);
        RefusalAssert.Is("IpmPanelTooNew", IpmCompatibility.CheckPanel(manifest with { MaxPanelVersion = "unreadable" }, TestPanel.Version, TestPanel.Now).Error);
    }

    /// <summary>A package is refused before its not-before instant and accepted at it, by the clock the caller passes.</summary>
    [Fact]
    public void A_package_is_not_valid_before_its_not_before_instant()
    {
        var manifest = Good() with { NotBefore = TestPanel.Now };

        Assert.True(IpmCompatibility.CheckPanel(manifest, TestPanel.Version, TestPanel.Now).IsSuccess);
        RefusalAssert.Is("IpmNotYetValid", IpmCompatibility.CheckPanel(manifest, TestPanel.Version, TestPanel.Now.AddSeconds(-1)).Error);
    }

    /// <summary>An older version than the installed one is refused unless the operator gives a reason.</summary>
    [Fact]
    public void A_downgrade_is_refused_without_a_reason_and_accepted_with_one()
    {
        var older = Good() with { Version = "1.1.0" };
        var installed = Installed(("hello", "1.2.0"));

        RefusalAssert.Is("IpmDowngradeRefused", IpmCompatibility.CheckInstall(older, installed, null).Error);
        RefusalAssert.Is("IpmDowngradeRefused", IpmCompatibility.CheckInstall(older, installed, new IpmDowngradeOverride("  ")).Error);
        Assert.True(IpmCompatibility.CheckInstall(older, installed, new IpmDowngradeOverride("the 1.2.0 release broke exports")).IsSuccess);
    }

    /// <summary>The same version, a newer one and a first install are not downgrades.</summary>
    [Fact]
    public void The_same_version_a_newer_one_and_a_first_install_are_not_downgrades()
    {
        var current = Good();

        Assert.True(IpmCompatibility.CheckInstall(current, Installed(("hello", "1.2.0")), null).IsSuccess);
        Assert.True(IpmCompatibility.CheckInstall(current, Installed(("hello", "1.1.9")), null).IsSuccess);
        Assert.True(IpmCompatibility.CheckInstall(current, Installed(), null).IsSuccess);
    }

    /// <summary>An installed version that cannot be read is treated as a downgrade, so it fails closed.</summary>
    [Fact]
    public void An_unreadable_installed_version_fails_closed()
    {
        RefusalAssert.Is("IpmDowngradeRefused", IpmCompatibility.CheckInstall(Good(), Installed(("hello", "garbage")), null).Error);
    }

    /// <summary>A dependency must be installed in a version its range accepts.</summary>
    [Fact]
    public void A_dependency_must_be_installed_in_an_accepted_version()
    {
        var manifest = Good() with { Dependencies = [new ModuleDependency("sites", ">=1.2.0 <2.0.0")] };

        Assert.True(IpmCompatibility.CheckInstall(manifest, Installed(("sites", "1.2.0")), null).IsSuccess);
        Assert.True(IpmCompatibility.CheckInstall(manifest, Installed(("sites", "1.9.9")), null).IsSuccess);
        RefusalAssert.Is("IpmDependencyUnsatisfied", IpmCompatibility.CheckInstall(manifest, Installed(("sites", "2.0.0")), null).Error);
        RefusalAssert.Is("IpmDependencyUnsatisfied", IpmCompatibility.CheckInstall(manifest, Installed(("sites", "1.1.9")), null).Error);
        RefusalAssert.Is("IpmDependencyUnsatisfied", IpmCompatibility.CheckInstall(manifest, Installed(), null).Error);
        RefusalAssert.Is("IpmDependencyUnsatisfied", IpmCompatibility.CheckInstall(manifest, Installed(("sites", "garbage")), null).Error);
    }

    /// <summary>The good manifest from the fixture builder.</summary>
    /// <returns>The manifest.</returns>
    private IpmManifest Good()
    {
        return new IpmFixtureBuilder(_key).BuildManifest();
    }

    /// <summary>An installation of the named modules.</summary>
    /// <param name="modules">Pairs of module id and installed version.</param>
    /// <returns>The installed modules.</returns>
    private static Dictionary<string, string> Installed(params (string Id, string Version)[] modules)
    {
        return modules.ToDictionary(
            module =>
            {
                return module.Id;
            },
            module =>
            {
                return module.Version;
            });
    }
}
