using NetArchTest.Rules;

namespace Maran.ArchitectureTests;

/// <summary>CI-enforced module isolation (rules/architecture.md).</summary>
public sealed class ModuleIsolationTests
{
    /// <summary>Modules reference only sdk and shared kernel.</summary>
    [Fact]
    public void Modules_reference_only_sdk_and_shared_kernel()
    {
        var result = Types.InCurrentDomain()
            .That().ResideInNamespaceStartingWith("Maran.Modules")
            .ShouldNot().HaveDependencyOnAny("Maran.Host")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>Shared kernel depends on nothing of ours.</summary>
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
