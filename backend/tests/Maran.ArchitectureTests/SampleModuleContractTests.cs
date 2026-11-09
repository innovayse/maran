using System.Globalization;
using System.Resources;
using Maran.Samples.Hello;
using Maran.Samples.Hello.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Versions;
using Microsoft.EntityFrameworkCore;

namespace Maran.ArchitectureTests;

/// <summary>
/// The sample module, <c>backend/samples/hello-module</c>, obeys the same module contract as the modules the panel
/// composes: it is the one packageable fixture the package and loader suites share, so it must be the
/// reference a module author can copy and not a second, laxer idea of a module.
/// </summary>
/// <remarks>
/// <para>
/// The sample is not a project of the solution and is outside <c>ModuleRegistry</c> on purpose (it is
/// packaged, not composed; this test project references it, so the solution build still compiles it), so none
/// of the census tests over the registry reaches it. These tests point the same laws at it:
/// <see cref="ModuleContractLaw"/> for the manifest, <see cref="ModuleReferenceLaw"/> for what its
/// project file and its compiled assembly reference, and a model check for the schema it owns.
/// </para>
/// <para>
/// The guards sit on the axes that can go blind: the contract law must have SEEN the controller's policy
/// (a law over a module whose controllers were not discovered would be satisfied by an empty set), and the
/// resource check reads the satellite assemblies the build copied, so a sample whose translations were never
/// compiled fails instead of being read as the neutral text three times.
/// </para>
/// </remarks>
public sealed class SampleModuleContractTests
{
    /// <summary>The embedded resource base name of the sample's display names.</summary>
    private const string DisplayNamesBaseName = "Maran.Samples.Hello.Resources.DisplayNames";

    /// <summary>The sample obeys the module contract.</summary>
    [Fact]
    public void The_sample_module_obeys_the_module_contract()
    {
        var subject = ModuleContractCensus.SubjectOf(new HelloModule());

        Assert.Equal(ModuleKind.Packageable, subject.Manifest.Kind);
        Assert.NotEmpty(subject.ControllerPolicies);
        var violations = ModuleContractLaw.Violations(
            [subject],
            ParsedPanelVersion(),
            ModuleContractCensus.PolicyVocabulary());

        Assert.True(
            violations.Count == 0,
            "The sample module breaks the module contract:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(violation => { return violation.Describe(); })));
    }

    /// <summary>The sample's project file references only what a packageable module may.</summary>
    [Fact]
    public void The_sample_project_file_references_only_what_a_packageable_module_may()
    {
        var violations = ModuleReferenceLaw.ProjectViolations(
            "hello",
            File.ReadAllText(SampleProjectPath()),
            declaresAgentCapabilities: false,
            ModuleReferenceAllowList.Load());

        Assert.Empty(violations);
    }

    /// <summary>The sample is compiled against the Sdk and the SharedKernel and no other product assembly.</summary>
    [Fact]
    public void The_sample_is_compiled_against_no_product_assembly_but_the_sdk_and_the_shared_kernel()
    {
        var references = typeof(HelloModule).Assembly.GetReferencedAssemblies()
            .Select(reference => { return reference.Name!; })
            .ToList();

        Assert.Contains("Maran.Sdk", references);
        Assert.Empty(ModuleReferenceLaw.CompiledViolations("hello", references, declaresAgentCapabilities: false));
    }

    /// <summary>The sample's context maps every table into the schema named by its manifest, which is its id.</summary>
    [Fact]
    public void The_sample_context_maps_every_table_into_the_schema_its_manifest_names()
    {
        var manifest = HelloManifest.Instance;
        using var context = new HelloDbContext(
            new DbContextOptionsBuilder<HelloDbContext>().UseNpgsql("Host=architecture.invalid;Database=maran").Options);
        var entities = context.Model.GetEntityTypes().ToList();

        Assert.NotEmpty(entities);
        Assert.Equal(manifest.Id, manifest.SchemaName);
        Assert.Equal(manifest.SchemaName, context.Model.GetDefaultSchema());
        Assert.All(entities, entity => { Assert.Equal(manifest.SchemaName, entity.GetSchema()); });
    }

    /// <summary>Every text key the manifest names resolves in all three languages.</summary>
    [Fact]
    public void Every_text_key_the_manifest_names_resolves_in_english_russian_and_armenian()
    {
        var manifest = HelloManifest.Instance;
        var keys = new[] { manifest.DisplayNameKey }
            .Concat(manifest.Menu.Select(item => { return item.LabelKey; }))
            .Concat(manifest.Routes.Select(route => { return route.TitleKey; }))
            .ToList();
        var resources = new ResourceManager(DisplayNamesBaseName, typeof(HelloModule).Assembly);

        Assert.True(keys.Count >= 3, "The manifest names fewer keys than the sample declares; the check read the wrong thing.");

        foreach (var key in keys)
        {
            foreach (var culture in new[] { "en", "ru", "hy" })
            {
                var text = resources.GetString(key, CultureInfo.GetCultureInfo(culture));
                Assert.False(string.IsNullOrWhiteSpace(text), $"Key '{key}' has no text in '{culture}'.");
            }

            Assert.NotEqual(
                resources.GetString(key, CultureInfo.GetCultureInfo("en")),
                resources.GetString(key, CultureInfo.GetCultureInfo("ru")));
        }
    }

    /// <summary>The path of the sample's project file, found from the backend root.</summary>
    /// <returns>Absolute path of <c>Maran.Samples.Hello.csproj</c>.</returns>
    private static string SampleProjectPath()
    {
        return Path.GetFullPath(Path.Combine(
            ResourceTriples.BackendRoot(), "samples", "hello-module", "Maran.Samples.Hello.csproj"));
    }

    /// <summary>The panel version this build is, parsed.</summary>
    /// <returns>The version the sample's minimum is judged against.</returns>
    private static SemanticVersion ParsedPanelVersion()
    {
        Assert.True(
            SemanticVersion.TryParse(ModuleContractCensus.PanelVersionText(), out var version),
            "The panel's own version is not a semantic version.");
        return version!;
    }
}
