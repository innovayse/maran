using System.Reflection;
using Maran.ArchitectureTests.Fixtures;
using Maran.Sdk.Interfaces;
using Xunit.Abstractions;

namespace Maran.ArchitectureTests;

/// <summary>
/// <c>Maran.Sdk</c> is the one project every module may reference, so it is the place a cross-module
/// shortcut would land. Its public surface is recorded exactly, and each module's use of it has a
/// ceiling that only goes down: growth is a reviewed edit of <c>Baselines/sdk-surface.txt</c>, never an
/// accident. There is deliberately no mode that refreshes the file from a test run.
/// </summary>
public sealed class SdkSurfaceTests
{
    /// <summary>The prefix every module assembly name carries.</summary>
    private const string ModulePrefix = "Maran.Modules.";

    /// <summary>Where a test writes what it would like the reviewer to see.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class.</summary>
    /// <param name="output">The xUnit output sink.</param>
    public SdkSurfaceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>Adding or removing a public Sdk type without editing the record turns this red.</summary>
    [Fact]
    public void Sdk_public_surface_equals_the_recorded_surface()
    {
        var actual = SdkSurfaceCensus.ExportedSurface(typeof(IPanelModule).Assembly);
        Assert.NotEmpty(actual);

        var differences = SdkSurfaceComparison.SurfaceDifferences(SdkSurfaceBaseline.Load().Types, actual);

        Assert.True(
            differences.Count == 0,
            "Maran.Sdk's public surface differs from " + SdkSurfaceBaseline.RelativePath + ". Growth of the Sdk is a "
            + "reviewed decision: edit the file in the same change and say in the pull request why the type "
            + "belongs in the project every module sees:\n" + string.Join("\n", differences)
            + "\n\nThe file's lines for the tree as it is now:\n" + RenderCurrent());
    }

    /// <summary>A module using more Sdk types than its recorded ceiling turns this red; using fewer only hints.</summary>
    [Fact]
    public void No_module_uses_more_Sdk_types_than_recorded()
    {
        var baseline = SdkSurfaceBaseline.Load();
        var usage = UsageByModule();

        var breaches = SdkSurfaceComparison.CeilingBreaches(baseline.ModuleCeilings, usage);
        var total = UnionUsage();
        if (total > baseline.TotalCeiling)
        {
            breaches.Add($"All modules together use {total} Sdk types, past the ceiling of {baseline.TotalCeiling}: 'total {total}'.");
        }

        foreach (var hint in SdkSurfaceComparison.LoweringHints(baseline.ModuleCeilings, usage))
        {
            _output.WriteLine("Lower the ceiling so the ratchet tightens: " + hint);
        }

        if (total < baseline.TotalCeiling)
        {
            _output.WriteLine($"Lower the ceiling so the ratchet tightens: 'total {baseline.TotalCeiling}' to 'total {total}'.");
        }

        Assert.True(
            breaches.Count == 0,
            "A module reaches for more of the Sdk than the record allows. Raising a ceiling is a reviewed "
            + "decision, edited in " + SdkSurfaceBaseline.RelativePath + " with the reason in the pull request:\n"
            + string.Join("\n", breaches));
    }

    /// <summary>A module loaded into the panel has a recorded ceiling, and a ceiling names a loaded module.</summary>
    [Fact]
    public void Every_module_has_a_recorded_ceiling()
    {
        var baseline = SdkSurfaceBaseline.Load();
        var usage = UsageByModule();
        Assert.NotEmpty(usage);

        var unrecorded = usage.Keys.Where(
            module =>
            {
                return !baseline.ModuleCeilings.ContainsKey(module);
            }).ToList();
        var stale = baseline.ModuleCeilings.Keys.Where(
            module =>
            {
                return !usage.ContainsKey(module);
            }).ToList();

        Assert.True(
            unrecorded.Count == 0 && stale.Count == 0,
            "Modules without a row in " + SdkSurfaceBaseline.RelativePath + ": " + string.Join(", ", unrecorded)
            + ". Rows naming no loaded module: " + string.Join(", ", stale) + ".");
    }

    /// <summary>Control: the reader sees a reference at all, using a type Identity certainly uses.</summary>
    [Fact]
    public void The_metadata_walk_sees_an_Sdk_type_the_Identity_module_certainly_uses()
    {
        var identity = ModuleAssemblies.Modules().Single(
            assembly =>
            {
                return assembly.GetName().Name == ModulePrefix + "Identity";
            });

        Assert.Contains(typeof(IPanelModule).FullName!, SdkSurfaceCensus.SdkTypesUsedBy(identity));
    }

    /// <summary>Control: a surface with one extra type is refused and the type is named.</summary>
    [Fact]
    public void A_surface_with_an_added_type_is_refused_by_name()
    {
        var recorded = new HashSet<string> { "Maran.Sdk.A", "Maran.Sdk.B" };

        var differences = SdkSurfaceComparison.SurfaceDifferences(recorded, ["Maran.Sdk.A", "Maran.Sdk.B", "Maran.Sdk.Extra"]);

        Assert.Equal(["add    type Maran.Sdk.Extra"], differences);
    }

    /// <summary>Control: a surface missing a recorded type is refused and the type is named.</summary>
    [Fact]
    public void A_surface_with_a_removed_type_is_refused_by_name()
    {
        var recorded = new HashSet<string> { "Maran.Sdk.A", "Maran.Sdk.B" };

        var differences = SdkSurfaceComparison.SurfaceDifferences(recorded, ["Maran.Sdk.A"]);

        Assert.Equal(["remove type Maran.Sdk.B"], differences);
    }

    /// <summary>Inverse control: an identical surface is accepted.</summary>
    [Fact]
    public void An_identical_surface_is_accepted()
    {
        var recorded = new HashSet<string> { "Maran.Sdk.A", "Maran.Sdk.B" };

        Assert.Empty(SdkSurfaceComparison.SurfaceDifferences(recorded, ["Maran.Sdk.B", "Maran.Sdk.A"]));
    }

    /// <summary>Control: one above a ceiling and a module without a row are refused; at or below is accepted.</summary>
    [Fact]
    public void A_module_one_above_its_ceiling_is_refused_and_one_at_or_below_is_accepted()
    {
        var ceilings = new Dictionary<string, int> { ["Over"] = 3, ["At"] = 3, ["Under"] = 3 };
        var usage = new Dictionary<string, int> { ["Over"] = 4, ["At"] = 3, ["Under"] = 2, ["New"] = 1 };

        var breaches = SdkSurfaceComparison.CeilingBreaches(ceilings, usage);

        Assert.Equal(2, breaches.Count);
        Assert.Contains(breaches, StartsWith("New has no recorded ceiling"));
        Assert.Contains(breaches, StartsWith("Over uses 4"));
        Assert.Equal(["lower 'module Under 3' to 'module Under 2'."], SdkSurfaceComparison.LoweringHints(ceilings, usage));
    }

    /// <summary>Control: a malformed line and a repeated line are each refused by naming the line.</summary>
    [Fact]
    public void A_malformed_or_repeated_baseline_line_is_refused_by_name()
    {
        var malformed = Assert.Throws<FormatException>(
            () =>
            {
                return SdkSurfaceBaseline.Parse("total 1\nmodule Sites many\n");
            });
        var repeated = Assert.Throws<FormatException>(
            () =>
            {
                return SdkSurfaceBaseline.Parse("total 1\ntype A\ntype A\n");
            });
        var accepted = SdkSurfaceBaseline.Parse("# note\ntotal 2\nmodule Sites 2\ntype A\n");

        Assert.Contains("line 2", malformed.Message);
        Assert.Contains("line 3", repeated.Message);
        Assert.Equal(2, accepted.TotalCeiling);
    }

    /// <summary>A predicate: the text begins with the given words.</summary>
    /// <param name="prefix">The words the text must begin with.</param>
    /// <returns>The predicate.</returns>
    private static Predicate<string> StartsWith(string prefix)
    {
        return text =>
        {
            return text.StartsWith(prefix, StringComparison.Ordinal);
        };
    }

    /// <summary>How many Sdk types each loaded module uses, keyed by the module's short name.</summary>
    /// <returns>The counts.</returns>
    private static Dictionary<string, int> UsageByModule()
    {
        return ModuleAssemblies.Modules().ToDictionary(
            ModuleName,
            assembly =>
            {
                return SdkSurfaceCensus.SdkTypesUsedBy(assembly).Count;
            },
            StringComparer.Ordinal);
    }

    /// <summary>How many distinct Sdk types the loaded modules use together.</summary>
    /// <returns>The size of the union.</returns>
    private static int UnionUsage()
    {
        return ModuleAssemblies.Modules()
            .SelectMany(
                assembly =>
                {
                    return SdkSurfaceCensus.SdkTypesUsedBy(assembly);
                })
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    /// <summary>The module's short name, without the assembly prefix.</summary>
    /// <param name="assembly">A module assembly.</param>
    /// <returns>For example <c>Sites</c>.</returns>
    private static string ModuleName(Assembly assembly)
    {
        return assembly.GetName().Name![ModulePrefix.Length..];
    }

    /// <summary>The record's lines for the tree as it is now.</summary>
    /// <returns>Text ready to paste under the file's header.</returns>
    private static string RenderCurrent()
    {
        return SdkSurfaceBaseline.Render(
            SdkSurfaceCensus.ExportedSurface(typeof(IPanelModule).Assembly),
            UsageByModule(),
            UnionUsage());
    }
}
