using Maran.Sdk.Contracts;
using Maran.Sdk.Manifests;
using Maran.Sdk.Manifests.Enums;
using Maran.Sdk.Navigation;
using Maran.SharedKernel.Utilities.Versions;

namespace Maran.Sdk.Tests.Manifests;

/// <summary>
/// The manifest rules, one finding at a time. Every rule that refuses has a test beside it handing the
/// same rule a manifest it must accept, so a validator changed to refuse everything cannot pass.
/// </summary>
public sealed class ManifestValidatorTests
{
    /// <summary>The panel version every test composes against.</summary>
    private static readonly SemanticVersion Panel = Parse("1.4.0");

    /// <summary>The policies a permission may name in these tests.</summary>
    private static readonly IReadOnlySet<string> Policies = ManifestPermissionVocabulary.Policies;

    /// <summary>Parses a version that must be valid.</summary>
    /// <param name="text">The version text.</param>
    /// <returns>The parsed version.</returns>
    private static SemanticVersion Parse(string text)
    {
        Assert.True(SemanticVersion.TryParse(text, out var version), text);
        return version!;
    }

    /// <summary>A manifest that breaks no rule: an owned schema, a screen, a menu entry and a policy.</summary>
    /// <param name="id">The module id, which is also its schema.</param>
    /// <returns>The manifest.</returns>
    private static Manifest Valid(string id = "sample")
    {
        return new Manifest(
            Id: id,
            DisplayNameKey: "SampleModuleDisplayName",
            Version: "1.0.0",
            Tier: LicenceTier.AddOn,
            Kind: ModuleKind.Packageable,
            MinPanelVersion: "1.0.0",
            SchemaName: id,
            Dependencies: [],
            AgentCapabilities: [],
            Permissions: [AuthorizationPolicies.AdminOnly],
            Menu: [new ModuleMenuItem(id + ".home", id + "Home", ModuleIconKeys.Grid, "SampleMenuLabel", 10, ModuleAudience.Everyone)],
            Routes: [new ModuleRoute(id + "Home", "/" + id, "SampleRouteTitle", null)],
            Audience: ModuleAudience.Everyone);
    }

    /// <summary>Judges one manifest, with the given ids composed beside it.</summary>
    /// <param name="manifest">The manifest to judge.</param>
    /// <param name="others">The ids of the other composed modules.</param>
    /// <returns>The findings.</returns>
    private static IReadOnlyList<ManifestFinding> Judge(Manifest manifest, params string[] others)
    {
        var known = others.Append(manifest.Id).ToHashSet(StringComparer.Ordinal);
        return ManifestValidator.Validate(manifest, Panel, known, Policies);
    }

    /// <summary>Judges a set of manifests.</summary>
    /// <param name="manifests">The manifests to judge.</param>
    /// <returns>The findings.</returns>
    private static IReadOnlyList<ManifestFinding> JudgeSet(params Manifest[] manifests)
    {
        return ManifestValidator.ValidateSet(manifests, Panel, Policies);
    }

    /// <summary>Whether the findings hold the kind for the subject.</summary>
    /// <param name="findings">The findings.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="subject">The offending value.</param>
    /// <returns>True when such a finding is present.</returns>
    private static bool Has(IReadOnlyList<ManifestFinding> findings, ManifestFindingKind kind, string subject)
    {
        return findings.Any(f => { return f.Kind == kind && f.Subject == subject; });
    }

    /// <summary>A manifest that breaks no rule produces no findings, singly and in a set.</summary>
    [Fact]
    public void A_manifest_that_breaks_no_rule_has_no_findings()
    {
        Assert.Empty(Judge(Valid()));
        Assert.Empty(JudgeSet(Valid("alpha"), Valid("beta")));
    }

    /// <summary>A module with no tables, no screen and no permission is as valid as one with all three.</summary>
    [Fact]
    public void A_module_with_no_schema_menu_routes_or_permissions_is_valid()
    {
        var bare = Valid() with { SchemaName = null, Permissions = [], Menu = [], Routes = [] };

        Assert.Empty(Judge(bare));
    }

    /// <summary>An id that is not lowercase letters and digits beginning with a letter is refused.</summary>
    [Theory]
    [InlineData("Cron")]
    [InlineData("1cron")]
    [InlineData("cron-job")]
    [InlineData("cron_job")]
    [InlineData("")]
    [InlineData("cronjob12345678901234567890123456789")]
    public void A_malformed_id_is_found(string id)
    {
        var findings = Judge(Valid() with { Id = id, SchemaName = null });

        Assert.True(Has(findings, ManifestFindingKind.IdMalformed, id));
    }

    /// <summary>Lowercase letters and digits beginning with a letter are accepted, up to the length limit.</summary>
    [Theory]
    [InlineData("cron")]
    [InlineData("php8")]
    [InlineData("a")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")]
    public void A_well_formed_id_is_accepted(string id)
    {
        Assert.Empty(Judge(Valid(id)));
    }

    /// <summary>A module version that is not semantic is refused; a pre-release version is accepted.</summary>
    [Fact]
    public void A_version_that_is_not_semantic_is_found_and_a_prerelease_is_not()
    {
        Assert.True(Has(Judge(Valid() with { Version = "1.0" }), ManifestFindingKind.VersionInvalid, "1.0"));
        Assert.True(Has(Judge(Valid() with { Version = "" }), ManifestFindingKind.VersionInvalid, ""));
        Assert.Empty(Judge(Valid() with { Version = "2.0.0-beta.1" }));
    }

    /// <summary>A minimum panel version that is not semantic is refused.</summary>
    [Fact]
    public void A_minimum_panel_version_that_is_not_semantic_is_found()
    {
        var findings = Judge(Valid() with { MinPanelVersion = "latest" });

        Assert.True(Has(findings, ManifestFindingKind.MinPanelVersionInvalid, "latest"));
        Assert.DoesNotContain(findings, f => { return f.Kind == ManifestFindingKind.MinPanelVersionAbovePanel; });
    }

    /// <summary>A module needing a newer panel is refused; one needing exactly this panel, or older, is accepted.</summary>
    [Fact]
    public void A_minimum_panel_version_above_the_panel_is_found_and_an_equal_one_is_not()
    {
        Assert.True(Has(
            Judge(Valid() with { MinPanelVersion = "1.4.1" }),
            ManifestFindingKind.MinPanelVersionAbovePanel,
            "1.4.1"));
        Assert.True(Has(
            Judge(Valid() with { MinPanelVersion = "2.0.0" }),
            ManifestFindingKind.MinPanelVersionAbovePanel,
            "2.0.0"));
        Assert.Empty(Judge(Valid() with { MinPanelVersion = "1.4.0" }));
        Assert.Empty(Judge(Valid() with { MinPanelVersion = "0.9.0" }));
    }

    /// <summary>A schema name that is not the id is refused; the id itself and no schema are accepted.</summary>
    [Fact]
    public void A_schema_name_that_is_not_the_id_is_found()
    {
        var findings = Judge(Valid("sample") with { SchemaName = "identity" });

        Assert.True(Has(findings, ManifestFindingKind.SchemaNameNotId, "identity"));
        Assert.Empty(Judge(Valid("sample") with { SchemaName = "sample" }));
        Assert.Empty(Judge(Valid("sample") with { SchemaName = null }));
    }

    /// <summary>A schema name differing from the id only by case is still not the id.</summary>
    [Fact]
    public void A_schema_name_differing_only_by_case_is_found()
    {
        Assert.True(Has(
            Judge(Valid("sample") with { SchemaName = "Sample" }),
            ManifestFindingKind.SchemaNameNotId,
            "Sample"));
    }

    /// <summary>A permission outside the policy vocabulary is refused; every policy in it is accepted.</summary>
    [Fact]
    public void A_permission_outside_the_vocabulary_is_found()
    {
        var findings = Judge(Valid() with { Permissions = [AuthorizationPolicies.AdminOnly, "sites.delete"] });

        Assert.True(Has(findings, ManifestFindingKind.PermissionUnknown, "sites.delete"));
        Assert.DoesNotContain(findings, f => { return f.Subject == AuthorizationPolicies.AdminOnly; });
        Assert.Empty(Judge(Valid() with { Permissions = ManifestPermissionVocabulary.Policies.ToArray() }));
    }

    /// <summary>A permission is compared exactly: a different case is a different name.</summary>
    [Fact]
    public void A_permission_is_compared_exactly()
    {
        Assert.True(Has(
            Judge(Valid() with { Permissions = ["adminonly"] }),
            ManifestFindingKind.PermissionUnknown,
            "adminonly"));
    }

    /// <summary>Two menu entries with one key in a module are refused; distinct keys are accepted.</summary>
    [Fact]
    public void A_duplicate_menu_key_inside_a_module_is_found_once()
    {
        var entry = new ModuleMenuItem("sample.a", "sampleHome", ModuleIconKeys.Grid, "Label", 1, ModuleAudience.Everyone);
        var findings = Judge(Valid() with { Menu = [entry, entry with { Order = 2 }, entry with { Order = 3 }] });

        Assert.Single(findings, f => { return f.Kind == ManifestFindingKind.DuplicateMenuKey; });
        Assert.Empty(Judge(Valid() with { Menu = [entry, entry with { Key = "sample.b" }] }));
    }

    /// <summary>Two routes with one name in a module are refused; distinct names are accepted.</summary>
    [Fact]
    public void A_duplicate_route_name_inside_a_module_is_found()
    {
        var route = new ModuleRoute("sampleHome", "/sample", "Title", null);

        Assert.True(Has(
            Judge(Valid() with { Routes = [route, route with { Path = "/other" }] }),
            ManifestFindingKind.DuplicateRouteName,
            "sampleHome"));
        Assert.Empty(Judge(Valid() with { Routes = [route, route with { Name = "sampleOther", Path = "/other" }] }));
    }

    /// <summary>A route requiring a module that is not composed, or the module itself, is refused; another composed module is accepted.</summary>
    [Fact]
    public void A_route_requiring_an_unknown_module_or_itself_is_found()
    {
        var route = new ModuleRoute("sampleHome", "/sample", "Title", "ghost");

        Assert.True(Has(
            Judge(Valid() with { Routes = [route] }),
            ManifestFindingKind.RouteRequiresUnknownModule,
            "ghost"));
        Assert.True(Has(
            Judge(Valid() with { Routes = [route with { RequiresModule = "sample" }] }),
            ManifestFindingKind.RouteRequiresUnknownModule,
            "sample"));
        Assert.Empty(Judge(Valid() with { Routes = [route with { RequiresModule = "sites" }] }, "sites"));
    }

    /// <summary>A dependency on a module that is not composed is refused; one that is composed is accepted.</summary>
    [Fact]
    public void A_dependency_on_a_module_that_is_not_composed_is_found()
    {
        var manifest = Valid() with { Dependencies = [new ModuleDependency("sites", ">=1.0.0")] };

        Assert.True(Has(Judge(manifest), ManifestFindingKind.DependencyUnknown, "sites"));
        Assert.Empty(Judge(manifest, "sites"));
    }

    /// <summary>A dependency range that does not parse is refused; a valid one is accepted.</summary>
    [Fact]
    public void A_dependency_range_that_is_not_valid_is_found()
    {
        var bad = Valid() with { Dependencies = [new ModuleDependency("sites", "^1.0.0")] };
        var good = Valid() with { Dependencies = [new ModuleDependency("sites", ">=1.0.0 <2.0.0")] };

        Assert.True(Has(Judge(bad, "sites"), ManifestFindingKind.DependencyRangeInvalid, "sites"));
        Assert.Empty(Judge(good, "sites"));
    }

    /// <summary>A module depending on itself is a cycle.</summary>
    [Fact]
    public void A_module_depending_on_itself_is_a_cycle()
    {
        var manifest = Valid() with { Dependencies = [new ModuleDependency("sample", ">=0.0.0")] };

        Assert.True(Has(Judge(manifest), ManifestFindingKind.DependencyCycle, "sample"));
        Assert.Single(JudgeSet(manifest), f => { return f.Kind == ManifestFindingKind.DependencyCycle; });
    }

    /// <summary>Two modules claiming one id are refused.</summary>
    [Fact]
    public void A_duplicate_module_id_is_found()
    {
        var findings = JudgeSet(Valid("alpha"), Valid("alpha") with { DisplayNameKey = "Other" }, Valid("beta"));

        Assert.Single(findings, f => { return f.Kind == ManifestFindingKind.DuplicateModuleId; });
        Assert.True(Has(findings, ManifestFindingKind.DuplicateModuleId, "alpha"));
    }

    /// <summary>A menu key two modules both claim is refused once, and is not also reported as a duplicate inside either.</summary>
    [Fact]
    public void A_menu_key_two_modules_claim_is_found_once()
    {
        var shared = new ModuleMenuItem("shared.key", "alphaHome", ModuleIconKeys.Grid, "Label", 1, ModuleAudience.Everyone);
        var alpha = Valid("alpha") with { Menu = [shared] };
        var beta = Valid("beta") with { Menu = [shared with { RouteName = "betaHome" }] };

        var findings = JudgeSet(alpha, beta);

        Assert.Single(findings, f => { return f.Kind == ManifestFindingKind.DuplicateMenuKey; });
        Assert.Equal("beta", findings.Single(f => { return f.Kind == ManifestFindingKind.DuplicateMenuKey; }).ModuleId);
        Assert.Empty(JudgeSet(alpha, beta with { Menu = [shared with { Key = "beta.key", RouteName = "betaHome" }] }));
    }

    /// <summary>A route name two modules both claim is refused; distinct names are accepted.</summary>
    [Fact]
    public void A_route_name_two_modules_claim_is_found()
    {
        var route = new ModuleRoute("home", "/home", "Title", null);
        var alpha = Valid("alpha") with { Routes = [route], Menu = [] };
        var beta = Valid("beta") with { Routes = [route with { Path = "/beta" }], Menu = [] };

        Assert.True(Has(JudgeSet(alpha, beta), ManifestFindingKind.DuplicateRouteName, "home"));
        Assert.Empty(JudgeSet(alpha, beta with { Routes = [route with { Name = "betaHome", Path = "/beta" }] }));
    }

    /// <summary>A dependency whose version is outside the range asked for is refused; inside it is accepted.</summary>
    [Fact]
    public void A_dependency_version_outside_its_range_is_found()
    {
        var sites = Valid("sites") with { Version = "1.5.0" };
        var inside = Valid("ssl") with { Dependencies = [new ModuleDependency("sites", ">=1.2.0 <2.0.0")] };
        var outside = Valid("ssl") with { Dependencies = [new ModuleDependency("sites", ">=2.0.0")] };

        Assert.Empty(JudgeSet(sites, inside));
        Assert.True(Has(JudgeSet(sites, outside), ManifestFindingKind.DependencyVersionUnsatisfied, "sites"));
    }

    /// <summary>A two-module cycle is found on both modules; the same modules in a chain are accepted.</summary>
    [Fact]
    public void A_two_module_cycle_is_found_on_both_and_a_chain_is_not()
    {
        var alpha = Valid("alpha") with { Dependencies = [new ModuleDependency("beta", ">=0.0.0")] };
        var beta = Valid("beta") with { Dependencies = [new ModuleDependency("alpha", ">=0.0.0")] };

        var findings = JudgeSet(alpha, beta);

        Assert.True(Has(findings, ManifestFindingKind.DependencyCycle, "alpha"));
        Assert.True(Has(findings, ManifestFindingKind.DependencyCycle, "beta"));
        Assert.Empty(JudgeSet(alpha, Valid("beta")));
    }

    /// <summary>A three-module cycle is found on all three, and a module merely leading into it is not flagged.</summary>
    [Fact]
    public void A_three_module_cycle_is_found_on_each_member_but_not_on_a_module_leading_into_it()
    {
        var a = Valid("alpha") with { Dependencies = [new ModuleDependency("beta", ">=0.0.0")] };
        var b = Valid("beta") with { Dependencies = [new ModuleDependency("gamma", ">=0.0.0")] };
        var c = Valid("gamma") with { Dependencies = [new ModuleDependency("alpha", ">=0.0.0")] };
        var entry = Valid("delta") with { Dependencies = [new ModuleDependency("alpha", ">=0.0.0")] };

        var findings = JudgeSet(a, b, c, entry);

        Assert.Equal(3, findings.Count(f => { return f.Kind == ManifestFindingKind.DependencyCycle; }));
        Assert.DoesNotContain(findings, f => { return f.ModuleId == "delta"; });
    }

    /// <summary>A core module resting on a packageable one is refused; the other directions are accepted.</summary>
    [Fact]
    public void A_core_module_depending_on_a_packageable_one_is_found()
    {
        var core = Valid("alpha") with
        {
            Kind = ModuleKind.Core,
            Dependencies = [new ModuleDependency("beta", ">=0.0.0")],
        };
        var packageable = Valid("beta");

        Assert.True(Has(JudgeSet(core, packageable), ManifestFindingKind.CoreDependsOnPackageable, "beta"));
        Assert.Empty(JudgeSet(core, packageable with { Kind = ModuleKind.Core }));
        Assert.Empty(JudgeSet(
            Valid("gamma") with { Dependencies = [new ModuleDependency("alpha", ">=0.0.0")] },
            core with { Dependencies = [] }));
    }

    /// <summary>An empty set has no findings; a set is judged module by module, so one bad member is named.</summary>
    [Fact]
    public void A_set_names_the_module_that_broke_a_rule()
    {
        Assert.Empty(JudgeSet());

        var findings = JudgeSet(Valid("alpha"), Valid("beta") with { Version = "nope" });

        Assert.Single(findings);
        Assert.Equal("beta", findings[0].ModuleId);
        Assert.Equal(ManifestFindingKind.VersionInvalid, findings[0].Kind);
    }
}
