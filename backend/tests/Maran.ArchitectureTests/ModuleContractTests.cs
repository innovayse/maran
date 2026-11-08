using Maran.ArchitectureTests.Fixtures;
using Maran.Host.Modules;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Versions;

namespace Maran.ArchitectureTests;

/// <summary>
/// Every module the panel composes obeys the module contract (docs/modules/module-contract.md): it
/// has a manifest, the manifest agrees with the module, its dependencies resolve and are acyclic, its
/// permissions are real policy names, and the Core/Packageable split holds.
/// </summary>
/// <remarks>
/// <para>
/// Each rule is shown on both sides. The real modules are judged by <see cref="ModuleContractLaw"/>
/// and must come out clean, which a law that had stopped looking would also report; so every rule has
/// a planted module (<see cref="PlantedBadManifestModule"/>) that must be flagged BY RULE NAME, and an
/// accepting control that must not be, because a law mutated to refuse everything passes every test
/// that only hands it broken input (rules/testing.md).
/// </para>
/// <para>
/// The vacuity guards sit on the axes that can go blind: the census is compared with a second source
/// (every module type compiled into a shipped assembly), and the strict rules are known to have run
/// over a non-empty packageable set.
/// </para>
/// </remarks>
public sealed class ModuleContractTests
{
    /// <summary>The panel version the planted modules are judged against.</summary>
    private const string PlantedPanelVersion = "2.0.0";

    /// <summary>The controller policies of a planted module: none, because it owns no controller.</summary>
    private static readonly IReadOnlySet<string> NoPolicies = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Every composed module obeys the contract.</summary>
    [Fact]
    public void Every_composed_module_obeys_the_module_contract()
    {
        var violations = ModuleContractLaw.Violations(
            ModuleContractCensus.Subjects(),
            ParsedPanelVersion(ModuleContractCensus.PanelVersionText()),
            ModuleContractCensus.PolicyVocabulary());

        Assert.True(
            violations.Count == 0,
            "A module breaks the module contract (docs/modules/module-contract.md):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(violation => { return violation.Describe(); })));
    }

    /// <summary>The census covers every module type the build ships, Licensing included.</summary>
    [Fact]
    public void The_census_covers_every_module_type_the_build_ships()
    {
        var shipped = ModuleContractCensus.ShippedModuleTypes()
            .Select(type => { return type.FullName!; })
            .ToHashSet(StringComparer.Ordinal);
        var examined = ModuleRegistry.All
            .Concat(ModuleContractCensus.UnregisteredModules())
            .Select(module => { return module.GetType().FullName!; })
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(shipped);
        Assert.True(
            shipped.SetEquals(examined),
            "The module types compiled into the build and the modules the contract is judged over differ. "
            + "Not judged: " + string.Join(", ", shipped.Except(examined, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            + ". Judged but not shipped: "
            + string.Join(", ", examined.Except(shipped, StringComparer.Ordinal).Order(StringComparer.Ordinal)) + ".");
    }

    /// <summary>The strict rules ran over both kinds of module, so neither half is judged over nothing.</summary>
    [Fact]
    public void The_census_examined_core_and_packageable_modules()
    {
        var kinds = ModuleContractCensus.Subjects()
            .Select(subject => { return subject.Manifest.Kind; })
            .ToHashSet();

        Assert.Contains(ModuleKind.Core, kinds);
        Assert.Contains(ModuleKind.Packageable, kinds);
    }

    /// <summary>A third module kind cannot appear without this suite being told.</summary>
    [Fact]
    public void Core_and_packageable_are_the_only_module_kinds()
    {
        var kinds = Enum.GetValues<ModuleKind>().Order().ToList();

        Assert.Equal(new[] { ModuleKind.Core, ModuleKind.Packageable }, kinds);
    }

    /// <summary>The policy vocabulary is read from the Sdk and holds the policies the panel defines.</summary>
    [Fact]
    public void The_policy_vocabulary_holds_the_policies_the_sdk_defines()
    {
        var vocabulary = ModuleContractCensus.PolicyVocabulary();

        Assert.Contains(AuthorizationPolicies.AdminOnly, vocabulary);
        Assert.Contains(AuthorizationPolicies.AnyAuthenticated, vocabulary);
        Assert.Contains(ApiKeyPolicyNames.SelfRead, vocabulary);
    }

    /// <summary>The accepting control: a core and a packageable module that obey every rule are not flagged.</summary>
    [Fact]
    public void Modules_that_obey_every_rule_are_accepted()
    {
        var violations = Judge(
            Planted("core", ModuleKind.Core),
            Planted("widgets", ModuleKind.Packageable));

        Assert.Empty(violations);
    }

    /// <summary>A module whose id differs from its name is flagged.</summary>
    [Fact]
    public void A_module_whose_id_differs_from_its_name_is_flagged()
    {
        var module = new PlantedBadManifestModule(
            "other",
            PlantedBadManifestModule.Valid("widgets", ModuleKind.Packageable));

        AssertFlagged(ModuleContractRule.IdIsNotName, "widgets", Judge(module));
    }

    /// <summary>An id that cannot be a schema name is flagged.</summary>
    [Fact]
    public void An_id_that_cannot_be_a_schema_name_is_flagged()
    {
        var manifest = PlantedBadManifestModule.Valid("Wid-gets", ModuleKind.Packageable) with { SchemaName = null };

        AssertFlagged(
            ModuleContractRule.IdIsNotSchemaShaped,
            "Wid-gets",
            Judge(new PlantedBadManifestModule("Wid-gets", manifest)));
    }

    /// <summary>Two modules publishing one id are both flagged.</summary>
    [Fact]
    public void Two_modules_publishing_one_id_are_flagged()
    {
        var violations = Judge(Planted("widgets", ModuleKind.Packageable), Planted("widgets", ModuleKind.Core));

        AssertFlagged(ModuleContractRule.DuplicateModuleId, "widgets", violations);
    }

    /// <summary>A module version that is not semantic is flagged.</summary>
    [Fact]
    public void A_module_version_that_is_not_semantic_is_flagged()
    {
        var manifest = PlantedBadManifestModule.Valid("widgets", ModuleKind.Packageable) with { Version = "one.two" };

        AssertFlagged(
            ModuleContractRule.VersionIsNotSemantic,
            "widgets",
            Judge(new PlantedBadManifestModule("widgets", manifest)));
    }

    /// <summary>A blank minimum panel version is flagged.</summary>
    [Fact]
    public void A_blank_minimum_panel_version_is_flagged()
    {
        var manifest = PlantedBadManifestModule.Valid("widgets", ModuleKind.Packageable) with { MinPanelVersion = " " };

        AssertFlagged(
            ModuleContractRule.MinPanelVersionIsNotSemantic,
            "widgets",
            Judge(new PlantedBadManifestModule("widgets", manifest)));
    }

    /// <summary>A minimum panel version above the panel is flagged.</summary>
    [Fact]
    public void A_minimum_panel_version_above_the_panel_is_flagged()
    {
        var manifest = PlantedBadManifestModule.Valid("widgets", ModuleKind.Packageable) with { MinPanelVersion = "9.0.0" };

        AssertFlagged(
            ModuleContractRule.MinPanelVersionAbovePanel,
            "widgets",
            Judge(new PlantedBadManifestModule("widgets", manifest)));
    }

    /// <summary>A module declaring another module's schema is flagged.</summary>
    [Fact]
    public void A_module_declaring_another_modules_schema_is_flagged()
    {
        var manifest = PlantedBadManifestModule.Valid("widgets", ModuleKind.Packageable) with { SchemaName = "identity" };

        AssertFlagged(
            ModuleContractRule.SchemaNameIsNotId,
            "widgets",
            Judge(new PlantedBadManifestModule("widgets", manifest)));
    }

    /// <summary>A module declaring no schema is accepted.</summary>
    [Fact]
    public void A_module_declaring_no_schema_is_accepted()
    {
        var manifest = PlantedBadManifestModule.Valid("widgets", ModuleKind.Packageable) with { SchemaName = null };

        Assert.Empty(Judge(new PlantedBadManifestModule("widgets", manifest)));
    }

    /// <summary>A permission that is not an Sdk policy name is flagged.</summary>
    [Fact]
    public void A_permission_that_is_not_an_sdk_policy_name_is_flagged()
    {
        var manifest = PlantedBadManifestModule.Valid("widgets", ModuleKind.Packageable) with
        {
            Permissions = [AuthorizationPolicies.AdminOnly, "Widgets.Frobnicate"],
        };

        AssertFlagged(
            ModuleContractRule.PermissionOutsideVocabulary,
            "widgets",
            Judge(new PlantedBadManifestModule("widgets", manifest)));
    }

    /// <summary>A packageable module's controller naming an undeclared policy is flagged.</summary>
    [Fact]
    public void A_packageable_controller_naming_an_undeclared_policy_is_flagged()
    {
        var subject = ModuleContractCensus.SubjectOf(Planted("widgets", ModuleKind.Packageable), NoPolicies) with
        {
            ControllerPolicies = new HashSet<string>([AuthorizationPolicies.AnyAuthenticated], StringComparer.Ordinal),
        };

        AssertFlagged(ModuleContractRule.ControllerPolicyNotDeclared, "widgets", JudgeSubjects(subject));
    }

    /// <summary>
    /// The same undeclared policy on a core module is accepted: the shipping rules bind packageable
    /// modules only, and this is the control that shows the module kind is what the rule reads.
    /// </summary>
    [Fact]
    public void A_core_controller_naming_an_undeclared_policy_is_accepted()
    {
        var subject = ModuleContractCensus.SubjectOf(Planted("core", ModuleKind.Core), NoPolicies) with
        {
            ControllerPolicies = new HashSet<string>([AuthorizationPolicies.AnyAuthenticated], StringComparer.Ordinal),
        };

        Assert.Empty(JudgeSubjects(subject));
    }

    /// <summary>A menu key shared by two modules flags both.</summary>
    [Fact]
    public void A_menu_key_shared_by_two_modules_is_flagged()
    {
        var first = Planted("widgets", ModuleKind.Packageable);
        var secondManifest = PlantedBadManifestModule.Valid("gadgets", ModuleKind.Packageable);
        var second = new PlantedBadManifestModule(
            "gadgets",
            secondManifest with { Menu = [secondManifest.Menu[0] with { Key = "widgets" }] });

        var violations = Judge(first, second);

        AssertFlagged(ModuleContractRule.DuplicateMenuKey, "widgets", violations);
        AssertFlagged(ModuleContractRule.DuplicateMenuKey, "gadgets", violations);
    }

    /// <summary>A route name shared by two modules flags both.</summary>
    [Fact]
    public void A_route_name_shared_by_two_modules_is_flagged()
    {
        var first = Planted("widgets", ModuleKind.Packageable);
        var secondManifest = PlantedBadManifestModule.Valid("gadgets", ModuleKind.Packageable);
        var second = new PlantedBadManifestModule(
            "gadgets",
            secondManifest with
            {
                Menu = [secondManifest.Menu[0] with { RouteName = "widgets" }],
                Routes = [secondManifest.Routes[0] with { Name = "widgets" }],
            });

        var violations = Judge(first, second);

        AssertFlagged(ModuleContractRule.DuplicateRouteName, "widgets", violations);
        AssertFlagged(ModuleContractRule.DuplicateRouteName, "gadgets", violations);
    }

    /// <summary>A packageable menu entry pointing at a route the module does not declare is flagged.</summary>
    [Fact]
    public void A_menu_entry_pointing_at_an_undeclared_route_is_flagged()
    {
        var manifest = PlantedBadManifestModule.Valid("widgets", ModuleKind.Packageable);
        manifest = manifest with { Menu = [manifest.Menu[0] with { RouteName = "elsewhere" }] };

        AssertFlagged(
            ModuleContractRule.MenuPointsAtNoRoute,
            "widgets",
            Judge(new PlantedBadManifestModule("widgets", manifest)));
    }

    /// <summary>A dependency on a module nobody publishes is flagged.</summary>
    [Fact]
    public void A_dependency_on_an_unknown_module_is_flagged()
    {
        var module = Depending("widgets", ModuleKind.Packageable, "ghosts");

        AssertFlagged(ModuleContractRule.DependencyUnknown, "widgets", Judge(module));
    }

    /// <summary>A module listing itself as a dependency is flagged.</summary>
    [Fact]
    public void A_module_depending_on_itself_is_flagged()
    {
        var module = Depending("widgets", ModuleKind.Packageable, "widgets");

        AssertFlagged(ModuleContractRule.DependencyOnSelf, "widgets", Judge(module));
    }

    /// <summary>Two modules depending on each other are both flagged as on a cycle.</summary>
    [Fact]
    public void A_dependency_cycle_is_flagged_on_every_module_in_it()
    {
        var violations = Judge(
            Depending("widgets", ModuleKind.Packageable, "gadgets"),
            Depending("gadgets", ModuleKind.Packageable, "widgets"));

        AssertFlagged(ModuleContractRule.DependencyCycle, "widgets", violations);
        AssertFlagged(ModuleContractRule.DependencyCycle, "gadgets", violations);
    }

    /// <summary>
    /// The accepting control for the dependency rules: a chain that ends in a core module, and a
    /// packageable module depending on another, are not flagged.
    /// </summary>
    [Fact]
    public void An_acyclic_chain_ending_in_a_core_module_is_accepted()
    {
        var violations = Judge(
            Planted("core", ModuleKind.Core),
            Depending("widgets", ModuleKind.Packageable, "core"),
            Depending("gadgets", ModuleKind.Packageable, "widgets"));

        Assert.Empty(violations);
    }

    /// <summary>A core module depending on a packageable one is flagged.</summary>
    [Fact]
    public void A_core_module_depending_on_a_packageable_one_is_flagged()
    {
        var violations = Judge(
            Planted("widgets", ModuleKind.Packageable),
            Depending("core", ModuleKind.Core, "widgets"));

        AssertFlagged(ModuleContractRule.CoreDependsOnPackageable, "core", violations);
    }

    /// <summary>A core module in a gated licence tier is flagged; the same tier on a packageable one is not.</summary>
    [Fact]
    public void A_gated_core_module_is_flagged_and_a_gated_packageable_one_is_accepted()
    {
        var gatedCore = PlantedBadManifestModule.Valid("core", ModuleKind.Core) with { Tier = LicenceTier.AddOn };
        var gatedPackage = PlantedBadManifestModule.Valid("widgets", ModuleKind.Packageable) with { Tier = LicenceTier.AddOn };

        AssertFlagged(
            ModuleContractRule.CoreIsGated,
            "core",
            Judge(new PlantedBadManifestModule("core", gatedCore)));
        Assert.Empty(Judge(new PlantedBadManifestModule("widgets", gatedPackage)));
    }

    /// <summary>Builds a planted module that obeys every rule.</summary>
    /// <param name="id">The module id.</param>
    /// <param name="kind">The module kind.</param>
    /// <returns>The planted module.</returns>
    private static PlantedBadManifestModule Planted(string id, ModuleKind kind)
    {
        return new PlantedBadManifestModule(id, PlantedBadManifestModule.Valid(id, kind));
    }

    /// <summary>Builds a planted module that obeys every rule except that it depends on another.</summary>
    /// <param name="id">The module id.</param>
    /// <param name="kind">The module kind.</param>
    /// <param name="dependencyId">The id it depends on.</param>
    /// <returns>The planted module.</returns>
    private static PlantedBadManifestModule Depending(string id, ModuleKind kind, string dependencyId)
    {
        var manifest = PlantedBadManifestModule.Valid(id, kind) with
        {
            Dependencies = [new ModuleDependency(Id: dependencyId, Range: ">=1.0.0")],
        };
        return new PlantedBadManifestModule(id, manifest);
    }

    /// <summary>Judges planted modules, read through the same path a real module takes.</summary>
    /// <param name="modules">The modules to judge.</param>
    /// <returns>The violations.</returns>
    private static IReadOnlyList<ModuleContractViolation> Judge(params PlantedBadManifestModule[] modules)
    {
        return JudgeSubjects(
            modules.Select(module => { return ModuleContractCensus.SubjectOf(module, NoPolicies); }).ToArray());
    }

    /// <summary>Judges subjects against the planted panel version and the real policy vocabulary.</summary>
    /// <param name="subjects">The subjects to judge.</param>
    /// <returns>The violations.</returns>
    private static IReadOnlyList<ModuleContractViolation> JudgeSubjects(params ModuleContractSubject[] subjects)
    {
        return ModuleContractLaw.Violations(
            subjects,
            ParsedPanelVersion(PlantedPanelVersion),
            ModuleContractCensus.PolicyVocabulary());
    }

    /// <summary>Asserts a rule is reported for a module.</summary>
    /// <param name="rule">The rule that must be named.</param>
    /// <param name="moduleId">The module it must be named for.</param>
    /// <param name="violations">What the law reported.</param>
    private static void AssertFlagged(
        ModuleContractRule rule,
        string moduleId,
        IReadOnlyList<ModuleContractViolation> violations)
    {
        Assert.True(
            violations.Any(violation =>
            {
                return violation.Rule == rule && string.Equals(violation.ModuleId, moduleId, StringComparison.Ordinal);
            }),
            $"Expected {rule} for '{moduleId}'; the law reported: "
            + (violations.Count == 0 ? "nothing" : string.Join("; ", violations.Select(violation => { return violation.Describe(); }))));
    }

    /// <summary>Parses a panel version, failing loudly when it is not one.</summary>
    /// <param name="text">The version text.</param>
    /// <returns>The parsed version.</returns>
    private static SemanticVersion ParsedPanelVersion(string text)
    {
        Assert.True(SemanticVersion.TryParse(text, out var version), $"The panel version '{text}' is not a semantic version.");
        return version!;
    }
}
