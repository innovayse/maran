using Maran.Host.Modules;
using Maran.Host.Search;
using Maran.Host.Tests.Modules.Fixtures;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Tests.Search;

/// <summary>Behavioural contract of <see cref="SearchModuleGate"/>: fail closed for a provider no composed module owns.</summary>
public sealed class SearchModuleGateTests
{
    /// <summary>The composition of a panel with no packaged module.</summary>
    private static readonly PackagedModuleComposition Shipped = new(ModuleRegistry.All, [], []);

    /// <summary>A provider owned by no composed module is not asked.</summary>
    [Fact]
    public void A_provider_that_belongs_to_no_composed_module_is_not_permitted()
    {
        Assert.False(SearchModuleGate.Permits(Shipped, new Orphan()));
    }

    /// <summary>Control: a provider of an enabled composed module is permitted.</summary>
    [Fact]
    public void A_provider_of_an_enabled_composed_module_is_permitted()
    {
        var provider = (ISearchProvider)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
            typeof(AccountsSearchProvider));

        Assert.True(SearchModuleGate.Permits(Shipped, provider));
    }

    /// <summary>A provider of a packaged module that joined the composition is permitted, though the shipped registry never held it.</summary>
    [Fact]
    public void A_provider_of_a_packaged_module_in_the_composition_is_permitted()
    {
        var packaged = new StubPackagedModule(TestManifests.Of("packaged"));
        var composition = new PackagedModuleComposition(
            [.. ModuleRegistry.All, packaged], [packaged], []);

        Assert.True(SearchModuleGate.Permits(composition, new Orphan()));
        Assert.False(SearchModuleGate.Permits(Shipped, new Orphan()));
    }

    /// <summary>A provider defined in the test assembly.</summary>
    private sealed class Orphan : ISearchProvider
    {
        /// <inheritdoc />
        public SearchKind Kind
        {
            get { return SearchKind.Site; }
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<SearchHit>>([]);
        }
    }
}
