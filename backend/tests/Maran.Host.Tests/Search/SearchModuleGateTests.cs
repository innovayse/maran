using Maran.Host.Modules;
using Maran.Host.Search;
using Maran.Host.Tests.Modules.Fixtures;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Tests.Search;

/// <summary>
/// Behavioural contract of <see cref="SearchModuleGate"/>: fail closed for a provider no composed module owns,
/// follow the licence window for a paid module, and never consult it for an included one.
/// </summary>
public sealed class SearchModuleGateTests
{
    /// <summary>The composition of a panel with no packaged module.</summary>
    private static readonly PackagedModuleComposition Shipped = new(ModuleRegistry.All, [], []);

    /// <summary>No verdicts: what a panel that composes no paid module reads.</summary>
    private static readonly IReadOnlyDictionary<string, EntitlementVerdict> NoVerdicts =
        new Dictionary<string, EntitlementVerdict>();

    /// <summary>A provider owned by no composed module is not asked.</summary>
    [Fact]
    public void A_provider_that_belongs_to_no_composed_module_is_not_permitted()
    {
        Assert.False(SearchModuleGate.Permits(Shipped, NoVerdicts, new Orphan()));
    }

    /// <summary>Control: a provider of an enabled composed module is permitted without any verdict.</summary>
    [Fact]
    public void A_provider_of_an_included_composed_module_is_permitted_with_no_verdict_at_all()
    {
        var provider = (ISearchProvider)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
            typeof(AccountsSearchProvider));

        Assert.True(SearchModuleGate.Permits(Shipped, NoVerdicts, provider));
    }

    /// <summary>A provider of a packaged module that joined the composition is permitted, though the shipped registry never held it.</summary>
    [Fact]
    public void A_provider_of_a_packaged_module_in_the_composition_is_permitted()
    {
        var composition = Composing(TestManifests.Of("packaged"));

        Assert.True(SearchModuleGate.Permits(composition, NoVerdicts, new Orphan()));
        Assert.False(SearchModuleGate.Permits(Shipped, NoVerdicts, new Orphan()));
    }

    /// <summary>A paid module is searchable only while the licence window covers it, with the inverse control in the same case.</summary>
    [Theory]
    [InlineData(EntitlementVerdict.Covered, true)]
    [InlineData(EntitlementVerdict.Lapsed, false)]
    [InlineData(EntitlementVerdict.NotCovered, false)]
    public void A_paid_module_is_searchable_only_while_the_licence_covers_it(EntitlementVerdict verdict, bool permitted)
    {
        var composition = Composing(TestManifests.Of("paid") with { Tier = LicenceTier.AddOn });
        var verdicts = new Dictionary<string, EntitlementVerdict> { ["paid"] = verdict };

        Assert.Equal(permitted, SearchModuleGate.Permits(composition, verdicts, new Orphan()));
    }

    /// <summary>A paid module the window did not name is not covered.</summary>
    [Fact]
    public void A_paid_module_the_window_did_not_name_is_not_permitted()
    {
        var composition = Composing(TestManifests.Of("paid") with { Tier = LicenceTier.PlanGated });

        Assert.False(SearchModuleGate.Permits(composition, NoVerdicts, new Orphan()));
    }

    /// <summary>Only paid modules are put to the window, and a panel with none never asks it.</summary>
    [Fact]
    public async Task Only_the_paid_modules_are_put_to_the_licence_window()
    {
        var window = new RecordingWindow(EntitlementVerdict.Covered);

        var shipped = await SearchModuleGate.VerdictsOfAsync(Shipped, window, CancellationToken.None);
        Assert.Empty(shipped);
        Assert.Equal(0, window.Reads);

        var composition = Composing(TestManifests.Of("paid") with { Tier = LicenceTier.AddOn });
        var mixed = await SearchModuleGate.VerdictsOfAsync(composition, window, CancellationToken.None);

        Assert.Equal(1, window.Reads);
        Assert.Equal(["paid"], window.Asked);
        Assert.Equal(EntitlementVerdict.Covered, mixed["paid"]);
    }

    /// <summary>The shipped modules plus one packaged module with the given manifest, which shares the test assembly.</summary>
    /// <param name="manifest">The packaged module's manifest.</param>
    /// <returns>The composition.</returns>
    private static PackagedModuleComposition Composing(Manifest manifest)
    {
        var packaged = new StubPackagedModule(manifest);

        return new PackagedModuleComposition([.. ModuleRegistry.All, packaged], [packaged], []);
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

    /// <summary>A licence window that answers one verdict for every module and remembers what it was asked.</summary>
    private sealed class RecordingWindow : ILicenceEntitlements
    {
        /// <summary>The verdict every module gets.</summary>
        private readonly EntitlementVerdict _verdict;

        /// <summary>Creates the window.</summary>
        /// <param name="verdict">The verdict every module gets.</param>
        public RecordingWindow(EntitlementVerdict verdict)
        {
            _verdict = verdict;
        }

        /// <summary>How many times the window was read.</summary>
        public int Reads { get; private set; }

        /// <summary>The ids of the last read.</summary>
        public IReadOnlyCollection<string> Asked { get; private set; } = [];

        /// <inheritdoc />
        public async Task<ModuleEntitlement> GetAsync(string moduleId, CancellationToken cancellationToken)
        {
            return (await ListAsync([moduleId], cancellationToken))[0];
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<ModuleEntitlement>> ListAsync(
            IReadOnlyCollection<string> moduleIds,
            CancellationToken cancellationToken)
        {
            Reads++;
            Asked = moduleIds;

            return Task.FromResult<IReadOnlyList<ModuleEntitlement>>(
                moduleIds.Select(id => { return new ModuleEntitlement(id, _verdict, null); }).ToList());
        }
    }
}
