using System.Security.Cryptography;
using Maran.Packaging.Models;
using Maran.Packaging.Services;
using Maran.Packaging.Tests.Builders;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Packaging.Tests.Services;

/// <summary>
/// The rules a package manifest must satisfy. Each refusal changes ONE thing in the good manifest, and
/// the unchanged good manifest is accepted by the same call, so a validator that refuses everything fails.
/// </summary>
public sealed class IpmManifestValidatorTests : IDisposable
{
    /// <summary>The throwaway key behind the fixture builder.</summary>
    private readonly ECDsa _key = IpmFixtureBuilder.NewKey();

    /// <summary>Frees the throwaway key.</summary>
    public void Dispose()
    {
        _key.Dispose();
    }

    /// <summary>The good manifest is accepted and is returned unchanged.</summary>
    [Fact]
    public void The_good_manifest_is_accepted()
    {
        var manifest = Good();

        var result = Judge(manifest);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Same(manifest, result.Value);
    }

    /// <summary>A module that declares what the Sdk's rules refuse is refused by the same rules.</summary>
    /// <param name="change">Names the one change that breaks a declaration rule.</param>
    [Theory]
    [InlineData("version-not-semantic")]
    [InlineData("id-malformed")]
    [InlineData("schema-not-the-id")]
    [InlineData("permission-unknown")]
    [InlineData("depends-on-itself")]
    [InlineData("dependency-range-unreadable")]
    [InlineData("route-name-twice")]
    public void A_declaration_the_module_rules_refuse_is_refused(string change)
    {
        var result = Judge(Break(change, Good()));

        RefusalAssert.Is("IpmManifestInvalid", result.Error);
    }

    /// <summary>A minimum panel above the running panel is the panel being too old, and a version that is not semantic is not.</summary>
    [Fact]
    public void A_minimum_panel_above_the_running_panel_is_reported_as_the_panel_being_too_old()
    {
        var tooOld = Judge(Good() with { MinPanelVersion = "9.0.0" });
        var atPanel = Judge(Good() with { MinPanelVersion = "1.4.0" });
        var unreadable = Judge(Good() with { MinPanelVersion = "one" });

        RefusalAssert.Is("IpmPanelTooOld", tooOld.Error);
        Assert.True(atPanel.IsSuccess);
        RefusalAssert.Is("IpmManifestInvalid", unreadable.Error);
    }

    /// <summary>A ceiling must be a version not below the minimum.</summary>
    [Fact]
    public void A_panel_ceiling_below_the_minimum_or_unreadable_is_refused_and_one_at_the_minimum_is_accepted()
    {
        Assert.True(Judge(Good() with { MaxPanelVersion = "1.0.0" }).IsSuccess);
        RefusalAssert.Is("IpmManifestInvalid", Judge(Good() with { MaxPanelVersion = "0.9.0" }).Error);
        RefusalAssert.Is("IpmManifestInvalid", Judge(Good() with { MaxPanelVersion = "later" }).Error);
    }

    /// <summary>A module owns a schema exactly when it carries migrations, and a migration id is a plain token.</summary>
    [Fact]
    public void Schema_and_migrations_must_agree()
    {
        var noSchemaNoMigrations = Good() with { SchemaName = null, Migrations = null };

        Assert.True(Judge(noSchemaNoMigrations).IsSuccess);
        RefusalAssert.Is("IpmManifestInvalid", Judge(Good() with { Migrations = null }).Error);
        RefusalAssert.Is("IpmManifestInvalid", Judge(noSchemaNoMigrations with { Migrations = new IpmMigrationRange(null, "X") }).Error);
        RefusalAssert.Is("IpmManifestInvalid", Judge(Good() with { Migrations = new IpmMigrationRange("a b", "X") }).Error);
        RefusalAssert.Is("IpmManifestInvalid", Judge(Good() with { Migrations = new IpmMigrationRange(null, new string('A', 151)) }).Error);
    }

    /// <summary>A pin with a path outside the layout, a traversal, a repeat or a malformed digest is refused.</summary>
    [Fact]
    public void A_file_listing_that_breaks_the_layout_is_refused()
    {
        var pin = new IpmFileEntry("lib/Extra.dll", IpmFixtureBuilder.Hex([1]), 1);

        Assert.True(Judge(WithFiles(pin)).IsSuccess);
        RefusalAssert.Is("IpmEntryTraversal", Judge(WithFiles(pin with { Path = "lib/../Extra.dll" })).Error);
        RefusalAssert.Is("IpmManifestInvalid", Judge(WithFiles(pin with { Path = "other/Extra.dll" })).Error);
        RefusalAssert.Is("IpmManifestInvalid", Judge(WithFiles(pin with { Path = "ipm.json" })).Error);
        RefusalAssert.Is("IpmManifestInvalid", Judge(WithFiles(pin with { Sha384 = "abc" })).Error);
        RefusalAssert.Is("IpmManifestInvalid", Judge(WithFiles(pin with { Sha384 = pin.Sha384.ToUpperInvariant() })).Error);
        RefusalAssert.Is("IpmManifestInvalid", Judge(WithFiles(pin with { Size = -1 })).Error);
        RefusalAssert.Is("IpmDuplicateEntry", Judge(WithFiles(pin, pin with { Path = "LIB/extra.dll" })).Error);
        RefusalAssert.Is("IpmManifestInvalid", Judge(Good() with { Files = [] }).Error);
    }

    /// <summary>The entry assembly must be a pinned file under lib/, and an encrypted package pins lib.enc and nothing under lib/.</summary>
    [Fact]
    public void The_library_listing_must_match_the_entry_assembly_and_the_encryption_declaration()
    {
        var withoutAssembly = Good() with { Files = FilesWithout("lib/") };
        var encrypted = Good() with
        {
            Encryption = new IpmEncryption("aes-256-gcm", "key-1"),
            Files = FilesWithout("lib/").Append(new IpmFileEntry("lib.enc", IpmFixtureBuilder.Hex([2]), 1)).ToList(),
        };

        RefusalAssert.Is("IpmManifestInvalid", Judge(withoutAssembly).Error);
        RefusalAssert.Is("IpmManifestInvalid", Judge(Good() with { EntryAssembly = "Hello.exe" }).Error);
        Assert.True(Judge(encrypted).IsSuccess);
        RefusalAssert.Is("IpmManifestInvalid", Judge(encrypted with { Encryption = new IpmEncryption("aes 256", "key-1") }).Error);
        RefusalAssert.Is("IpmManifestInvalid", Judge(Good() with { Encryption = new IpmEncryption("aes-256-gcm", "key-1") }).Error);
        RefusalAssert.Is("IpmManifestInvalid", Judge(WithFiles(new IpmFileEntry("lib.enc", IpmFixtureBuilder.Hex([2]), 1))).Error);
    }

    /// <summary>The interface bundle must be consistent: its entry pinned, its integrity the entry's own pin, the fallback locale present and every locale pinned.</summary>
    [Fact]
    public void The_interface_bundle_must_be_consistent_with_its_pins()
    {
        var spa = Good().Spa!;

        Assert.True(Judge(Good() with { Spa = null, Files = FilesWithout("spa/") }).IsSuccess);
        RefusalAssert.Is("IpmManifestInvalid", Judge(Good() with { Spa = null }).Error);
        RefusalAssert.Is("IpmManifestInvalid", Judge(Good() with { Spa = spa with { Integrity = "sha384-AAAA" } }).Error);
        RefusalAssert.Is("IpmManifestInvalid", Judge(Good() with { Spa = spa with { Entry = "spa/missing.js" } }).Error);
        RefusalAssert.Is("IpmManifestInvalid", Judge(Good() with { Spa = spa with { Locales = [] } }).Error);
        RefusalAssert.Is("IpmManifestInvalid", Judge(Good() with { Spa = spa with { Locales = ["en", "en"] } }).Error);
        RefusalAssert.Is("IpmManifestInvalid", Judge(Good() with { Spa = spa with { Locales = ["en", "ru"] } }).Error);
        RefusalAssert.Is("IpmManifestInvalid", Judge(Good() with { Spa = spa with { Locales = ["EN"] } }).Error);
    }

    /// <summary>Applies the one change a case names to a manifest.</summary>
    /// <param name="change">The case name.</param>
    /// <param name="manifest">The good manifest.</param>
    /// <returns>The manifest with exactly that rule broken.</returns>
    private static IpmManifest Break(string change, IpmManifest manifest)
    {
        return change switch
        {
            "version-not-semantic" => manifest with { Version = "1.0" },
            "id-malformed" => manifest with { Id = "Hello World" },
            "schema-not-the-id" => manifest with { SchemaName = "other" },
            "permission-unknown" => manifest with { Permissions = ["SuperUser"] },
            "depends-on-itself" => manifest with { Dependencies = [new ModuleDependency("hello", ">=1.0.0")] },
            "dependency-range-unreadable" => manifest with { Dependencies = [new ModuleDependency("other", "newest")] },
            "route-name-twice" => manifest with { Routes = [manifest.Routes[0], manifest.Routes[0]] },
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, "No such case."),
        };
    }

    /// <summary>The good manifest's files, leaving out those under a prefix.</summary>
    /// <param name="prefix">The folder prefix to leave out.</param>
    /// <returns>The remaining files.</returns>
    private List<IpmFileEntry> FilesWithout(string prefix)
    {
        return Good().Files
            .Where(file =>
            {
                return !file.Path.StartsWith(prefix, StringComparison.Ordinal);
            })
            .ToList();
    }

    /// <summary>Judges a manifest against the test panel.</summary>
    /// <param name="manifest">The manifest.</param>
    /// <returns>The validator's answer.</returns>
    private static Result<IpmManifest> Judge(IpmManifest manifest)
    {
        return IpmManifestValidator.Validate(manifest, TestPanel.Version);
    }

    /// <summary>The good manifest from the fixture builder.</summary>
    /// <returns>The manifest.</returns>
    private IpmManifest Good()
    {
        return new IpmFixtureBuilder(_key).BuildManifest();
    }

    /// <summary>The good manifest with more pinned files.</summary>
    /// <param name="extra">The files to add.</param>
    /// <returns>The manifest.</returns>
    private IpmManifest WithFiles(params IpmFileEntry[] extra)
    {
        var good = Good();
        return good with { Files = good.Files.Concat(extra).ToList() };
    }
}
