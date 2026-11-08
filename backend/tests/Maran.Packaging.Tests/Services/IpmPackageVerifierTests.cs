using System.Security.Cryptography;
using System.Text;
using Maran.Packaging.Models;
using Maran.Packaging.Options;
using Maran.Packaging.Services;
using Maran.Packaging.Tests.Builders;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Packaging.Tests.Services;

/// <summary>
/// Every refusal of the whole verification, one planted defect per test. Each is asserted by its code,
/// and the same call with the good package accepts, so a verifier that refuses everything fails them all.
/// The signing key is a throwaway generated for the run; no key is committed.
/// </summary>
public sealed class IpmPackageVerifierTests : IDisposable
{
    /// <summary>The key the panel trusts in these tests.</summary>
    private readonly ECDsa _key = IpmFixtureBuilder.NewKey();

    /// <summary>The verifier under test, trusting <see cref="_key"/> only.</summary>
    private readonly IpmPackageVerifier _verifier;

    /// <summary>Builds a verifier that trusts the throwaway key.</summary>
    public IpmPackageVerifierTests()
    {
        _verifier = new IpmPackageVerifier(
            new IpmReader(IpmLimits.Default),
            IpmSignatureVerifier.FromKey(_key).Value);
    }

    /// <summary>Frees the throwaway key.</summary>
    public void Dispose()
    {
        _key.Dispose();
    }

    /// <summary>The good package passes every check and comes back with its manifest and payload.</summary>
    [Fact]
    public void The_good_package_is_accepted()
    {
        var result = Verify(new IpmFixtureBuilder(_key));

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal("hello", result.Value.Manifest.Id);
        Assert.Equal(3, result.Value.Archive.Payload.Count);
    }

    /// <summary>A file added to the archive that the manifest does not list is refused.</summary>
    [Fact]
    public void An_undeclared_extra_file_is_refused()
    {
        var result = Verify(new IpmFixtureBuilder(_key).WithUnlistedFile("lib/Extra.dll", [1, 2, 3]));

        RefusalAssert.Is("IpmUnlistedEntry", result.Error);
    }

    /// <summary>A file the manifest lists but the archive lacks is refused.</summary>
    [Fact]
    public void A_missing_file_is_refused()
    {
        var result = Verify(new IpmFixtureBuilder(_key).WithoutStoredFile("lib/Hello.dll"));

        RefusalAssert.Is("IpmMissingEntry", result.Error);
    }

    /// <summary>A file whose bytes changed after the manifest pinned them is refused, even by one byte.</summary>
    [Fact]
    public void A_file_whose_hash_differs_from_its_pin_is_refused()
    {
        var result = Verify(new IpmFixtureBuilder(_key)
            .WithTamperedFile("lib/Hello.dll", Encoding.UTF8.GetBytes("assembly byteS")));

        RefusalAssert.Is("IpmHashMismatch", result.Error);
    }

    /// <summary>A file whose size differs from the size its pin states is refused.</summary>
    [Fact]
    public void A_file_whose_size_differs_from_its_pin_is_refused()
    {
        var result = Verify(new IpmFixtureBuilder(_key).WithManifest(manifest =>
        {
            var files = manifest.Files.ToList();
            files[0] = files[0] with { Size = files[0].Size + 1 };
            return manifest with { Files = files };
        }));

        RefusalAssert.Is("IpmHashMismatch", result.Error);
    }

    /// <summary>A manifest format revision this panel does not know is refused, signed or not.</summary>
    [Fact]
    public void An_unknown_format_version_is_refused()
    {
        var result = Verify(new IpmFixtureBuilder(_key).WithManifest(manifest =>
        {
            return manifest with { FormatVersion = 2 };
        }));

        RefusalAssert.Is("IpmFormatVersionUnknown", result.Error);
    }

    /// <summary>A module version that is not semantic is refused.</summary>
    [Fact]
    public void A_version_that_is_not_semantic_is_refused()
    {
        var result = Verify(new IpmFixtureBuilder(_key).WithManifest(manifest =>
        {
            return manifest with { Version = "1.2" };
        }));

        RefusalAssert.Is("IpmManifestInvalid", result.Error);
    }

    /// <summary>A package that needs a newer panel than the running one is refused as the panel being too old.</summary>
    [Fact]
    public void A_minimum_panel_above_the_running_panel_is_refused()
    {
        var result = Verify(new IpmFixtureBuilder(_key).WithManifest(manifest =>
        {
            return manifest with { MinPanelVersion = "9.0.0" };
        }));

        RefusalAssert.Is("IpmPanelTooOld", result.Error);
    }

    /// <summary>A manifest changed by one byte after signing is refused for its signature and never parsed.</summary>
    [Fact]
    public void A_manifest_tampered_by_one_byte_is_refused_for_its_signature()
    {
        var result = Verify(new IpmFixtureBuilder(_key).WithTamperedManifestByte());

        RefusalAssert.Is("IpmSignatureInvalid", result.Error);
    }

    /// <summary>A package signed by a key the panel does not trust is refused.</summary>
    [Fact]
    public void A_signature_by_another_key_is_refused()
    {
        using var other = IpmFixtureBuilder.NewKey();

        var result = Verify(new IpmFixtureBuilder(_key).SignedBy(other));

        RefusalAssert.Is("IpmSignatureInvalid", result.Error);
    }

    /// <summary>A package with no signature at all is refused.</summary>
    [Fact]
    public void An_unsigned_package_is_refused()
    {
        var result = Verify(new IpmFixtureBuilder(_key).WithoutSignature());

        RefusalAssert.Is("IpmSignatureMissing", result.Error);
    }

    /// <summary>A package that is not an archive is refused as data, and nothing is thrown at the caller.</summary>
    [Fact]
    public void Bytes_that_are_not_an_archive_are_refused_without_throwing()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('z', 300)));

        var result = _verifier.Verify(stream, TestPanel.Alone());

        RefusalAssert.Is("IpmArchiveMalformed", result.Error);
    }

    /// <summary>A traversal entry is refused by the whole verification, not only by the reader.</summary>
    [Fact]
    public void A_traversal_entry_is_refused()
    {
        var result = Verify(new IpmFixtureBuilder(_key).WithRawEntry("../../etc/cron.d/evil", [1]));

        RefusalAssert.Is("IpmEntryTraversal", result.Error);
    }

    /// <summary>A package newer than its ceiling allows, or not yet valid, is refused; at the boundary it is accepted.</summary>
    [Fact]
    public void A_panel_above_the_ceiling_and_a_package_not_yet_valid_are_refused()
    {
        var tooNew = Verify(new IpmFixtureBuilder(_key).WithManifest(manifest =>
        {
            return manifest with { MaxPanelVersion = "1.3.0" };
        }));
        var early = Verify(new IpmFixtureBuilder(_key).WithManifest(manifest =>
        {
            return manifest with { NotBefore = TestPanel.Now.AddDays(1) };
        }));
        var atBoundary = Verify(new IpmFixtureBuilder(_key).WithManifest(manifest =>
        {
            return manifest with { MaxPanelVersion = "1.4.0", NotBefore = TestPanel.Now };
        }));

        RefusalAssert.Is("IpmPanelTooNew", tooNew.Error);
        RefusalAssert.Is("IpmNotYetValid", early.Error);
        Assert.True(atBoundary.IsSuccess, atBoundary.Error?.Code);
    }

    /// <summary>Against an installation, a downgrade is refused unless reasoned, and a missing dependency is refused.</summary>
    [Fact]
    public void An_installation_refuses_a_downgrade_and_an_unmet_dependency()
    {
        var installed = new Dictionary<string, string> { ["hello"] = "1.3.0" };

        var downgrade = Verify(NewBuilder(), TestPanel.Installed(installed));
        var reasoned = Verify(NewBuilder(), TestPanel.Installed(installed, new IpmDowngradeOverride("rollback of a bad release")));
        var unmet = Verify(
            NewBuilder().WithManifest(manifest =>
            {
                return manifest with { Dependencies = [new ModuleDependency("sites", ">=1.0.0")] };
            }),
            TestPanel.Installed(new Dictionary<string, string>()));

        RefusalAssert.Is("IpmDowngradeRefused", downgrade.Error);
        Assert.True(reasoned.IsSuccess, reasoned.Error?.Code);
        RefusalAssert.Is("IpmDependencyUnsatisfied", unmet.Error);
    }

    /// <summary>Judged on its own, with no installation, a package that names a dependency is not refused for it being absent.</summary>
    [Fact]
    public void A_package_judged_on_its_own_is_not_refused_for_an_absent_dependency()
    {
        var result = Verify(new IpmFixtureBuilder(_key).WithManifest(manifest =>
        {
            return manifest with { Dependencies = [new ModuleDependency("sites", ">=1.0.0")] };
        }));

        Assert.True(result.IsSuccess, result.Error?.Code);
    }

    /// <summary>The verifier has no key of its own: a verifier built on another key refuses the good package.</summary>
    [Fact]
    public void A_verifier_trusting_another_key_refuses_the_good_package()
    {
        using var other = IpmFixtureBuilder.NewKey();
        var stranger = new IpmPackageVerifier(
            new IpmReader(IpmLimits.Default),
            IpmSignatureVerifier.FromKey(other).Value);
        using var stream = new MemoryStream(new IpmFixtureBuilder(_key).Build());

        var result = stranger.Verify(stream, TestPanel.Alone());

        RefusalAssert.Is("IpmSignatureInvalid", result.Error);
    }

    /// <summary>A builder for the good package, signed with the trusted key.</summary>
    /// <returns>The builder.</returns>
    private IpmFixtureBuilder NewBuilder()
    {
        return new IpmFixtureBuilder(_key);
    }

    /// <summary>Verifies a built package on its own, against the test panel.</summary>
    /// <param name="builder">The package to build and verify.</param>
    /// <param name="context">What to judge it against; the package on its own when null.</param>
    /// <returns>The verifier's answer.</returns>
    private Result<VerifiedIpmPackage> Verify(IpmFixtureBuilder builder, IpmVerificationContext? context = null)
    {
        using var stream = new MemoryStream(builder.Build());
        return _verifier.Verify(stream, context ?? TestPanel.Alone());
    }
}
