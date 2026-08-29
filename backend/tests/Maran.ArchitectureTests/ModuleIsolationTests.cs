using NetArchTest.Rules;

namespace Maran.ArchitectureTests;

/// <summary>CI-enforced module isolation (rules/architecture.md).</summary>
public sealed class ModuleIsolationTests
{
    [Fact]
    public void Modules_reference_only_sdk_and_shared_kernel()
    {
        var result = Types.InCurrentDomain()
            .That().ResideInNamespaceStartingWith("Maran.Modules")
            .ShouldNot().HaveDependencyOnAny("Maran.Host")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Shared_kernel_depends_on_nothing_of_ours()
    {
        var result = Types.InCurrentDomain()
            .That().ResideInNamespaceStartingWith("Maran.SharedKernel")
            .ShouldNot().HaveDependencyOnAny("Maran.Host", "Maran.Sdk", "Maran.Modules", "Maran.Agent.Client")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
