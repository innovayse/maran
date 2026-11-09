using Maran.Packaging.Services;

namespace Maran.ArchitectureTests;

/// <summary>
/// <c>Maran.Packaging</c>, the open library that decides whether a module package is acceptable, depends on
/// nothing of the Host and on no module, and brings no package of its own (docs/marketplace/ipm-format.md).
/// </summary>
/// <remarks>
/// Each rule is shown on both sides. The real library is judged by <see cref="PackagingReferenceLaw"/> and
/// must come out clean, which a law that had stopped reading would also report; so a planted project file and
/// a planted list of compiled references must be flagged BY RULE NAME, and a clean one must not be, because
/// a law mutated to refuse everything passes every test that only hands it broken input (rules/testing.md).
/// The census guard sits on the axis that can go blind: the project file must name the two projects it is
/// allowed, so a moved file or a changed spelling is not read as an empty, clean library.
/// </remarks>
public sealed class PackagingIsolationTests
{
    /// <summary>A library project file that references the Host, a module, the agent client and a package.</summary>
    private const string BadProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <ProjectReference Include="..\Maran.Host\Maran.Host.csproj" />
            <ProjectReference Include="..\Maran.Modules\Accounts\Maran.Modules.Accounts.csproj" />
            <ProjectReference Include="..\Maran.Agent.Client\Maran.Agent.Client.csproj" />
            <ProjectReference Include="..\Maran.Sdk\Maran.Sdk.csproj" />
            <PackageReference Include="Newtonsoft.Json" />
          </ItemGroup>
        </Project>
        """;

    /// <summary>A library project file that obeys the rules.</summary>
    private const string CleanProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <ProjectReference Include="..\Maran.Sdk\Maran.Sdk.csproj" />
            <ProjectReference Include="..\Maran.SharedKernel\Maran.SharedKernel.csproj" />
          </ItemGroup>
        </Project>
        """;

    /// <summary>The library's project file references only the Sdk and the SharedKernel, and no package.</summary>
    [Fact]
    public void The_packaging_project_file_references_only_the_sdk_and_the_shared_kernel()
    {
        var text = File.ReadAllText(ProjectPath());

        Assert.Contains("Maran.Sdk.csproj", text, StringComparison.Ordinal);
        Assert.Contains("Maran.SharedKernel.csproj", text, StringComparison.Ordinal);
        Assert.Empty(PackagingReferenceLaw.ProjectViolations(text));
    }

    /// <summary>The library is compiled against no product assembly but the Sdk and the SharedKernel.</summary>
    [Fact]
    public void The_packaging_assembly_is_compiled_against_no_product_assembly_but_the_sdk_and_the_shared_kernel()
    {
        var references = typeof(IpmReader).Assembly.GetReferencedAssemblies()
            .Select(reference => { return reference.Name!; })
            .ToList();

        Assert.Contains("Maran.Sdk", references);
        Assert.Empty(PackagingReferenceLaw.CompiledViolations(references));
    }

    /// <summary>A planted project file is flagged by rule, and a clean one is accepted.</summary>
    [Fact]
    public void A_planted_project_file_is_flagged_by_rule_and_a_clean_one_is_accepted()
    {
        var violations = PackagingReferenceLaw.ProjectViolations(BadProject);

        Assert.Contains(violations, line => { return line.StartsWith(PackagingReferenceLaw.ProjectReferenceNotAllowed, StringComparison.Ordinal) && line.Contains("Maran.Host", StringComparison.Ordinal); });
        Assert.Contains(violations, line => { return line.StartsWith(PackagingReferenceLaw.ProjectReferenceNotAllowed, StringComparison.Ordinal) && line.Contains("Maran.Modules.Accounts", StringComparison.Ordinal); });
        Assert.Contains(violations, line => { return line.StartsWith(PackagingReferenceLaw.ProjectReferenceNotAllowed, StringComparison.Ordinal) && line.Contains("Maran.Agent.Client", StringComparison.Ordinal); });
        Assert.Contains(violations, line => { return line.StartsWith(PackagingReferenceLaw.PackageNotAllowed, StringComparison.Ordinal); });
        Assert.Empty(PackagingReferenceLaw.ProjectViolations(CleanProject));
    }

    /// <summary>A planted list of compiled references is flagged by rule, and the allowed ones are accepted.</summary>
    [Fact]
    public void A_planted_compiled_reference_is_flagged_by_rule_and_the_allowed_ones_are_accepted()
    {
        var flagged = PackagingReferenceLaw.CompiledViolations(
            ["System.Runtime", "Maran.Sdk", "Maran.Host", "Maran.Modules.Cron", "Maran.Agent.Client"]);
        var accepted = PackagingReferenceLaw.CompiledViolations(
            ["System.Runtime", "Maran.Sdk", "Maran.SharedKernel", "Microsoft.Extensions.DependencyInjection.Abstractions"]);

        Assert.Equal(3, flagged.Count);
        Assert.All(flagged, line => { Assert.StartsWith(PackagingReferenceLaw.CompiledReferenceNotAllowed, line, StringComparison.Ordinal); });
        Assert.Empty(accepted);
    }

    /// <summary>The path of the library's project file, found from the backend root.</summary>
    /// <returns>Absolute path of <c>Maran.Packaging.csproj</c>.</returns>
    private static string ProjectPath()
    {
        return Path.Combine(ResourceTriples.BackendRoot(), "src", "Maran.Packaging", "Maran.Packaging.csproj");
    }
}
