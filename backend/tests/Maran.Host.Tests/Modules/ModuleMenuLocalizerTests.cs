using System.Diagnostics.CodeAnalysis;
using Maran.Host.Modules;
using Maran.Host.Tests.Modules.Fixtures;
using Maran.Sdk.Contracts;
using Maran.Sdk.Navigation;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Host.Tests.Modules;

/// <summary>
/// Behavioral contract of <see cref="ModuleMenuLocalizer"/>: a module's menu is resolved through the
/// text provider, ordered, and cut to the entries the caller is offered.
/// </summary>
public sealed class ModuleMenuLocalizerTests
{
    /// <summary>A provider that renders a key as <c>text:key</c>, so a label that was never resolved is visible.</summary>
    private sealed class MarkingTextProvider : IErrorTextProvider
    {
        /// <inheritdoc />
        public string Resolve(string code, params object[] arguments)
        {
            return "text:" + code;
        }

        /// <inheritdoc />
        public bool TryResolve(string code, [NotNullWhen(true)] out string? text)
        {
            text = "text:" + code;
            return true;
        }
    }

    /// <summary>Builds a manifest with three menu entries addressed to three audiences, declared out of order.</summary>
    /// <returns>The manifest.</returns>
    private static Manifest ThreeEntries()
    {
        return TestManifests.Of(
            "reports",
            menu:
            [
                new ModuleMenuItem("reports.admin", "reports-admin", "gear", "ReportsAdminLabel", 30, ModuleAudience.AdministratorOnly),
                new ModuleMenuItem("reports.home", "reports-home", "chart", "ReportsHomeLabel", 20, ModuleAudience.Everyone),
                new ModuleMenuItem("reports.team", "reports-team", "users", "ReportsTeamLabel", 10, ModuleAudience.AdminOrReseller),
            ]);
    }

    /// <summary>An administrator is offered every entry, localized and ordered by their declared order.</summary>
    [Fact]
    public void An_administrator_is_offered_every_entry_localized_and_in_declared_order()
    {
        var menu = ModuleMenuLocalizer.MenuOf(
            ThreeEntries(),
            new AdministratorCaller(Guid.NewGuid(), "root"),
            new MarkingTextProvider());

        Assert.Equal(["reports.team", "reports.home", "reports.admin"], menu.Select(item => { return item.Key; }));
        Assert.Equal("text:ReportsTeamLabel", menu[0].Label);
        Assert.Equal("reports-team", menu[0].RouteName);
        Assert.Equal("users", menu[0].IconKey);
    }

    /// <summary>A customer is offered only the entry addressed to everyone, and a reseller the entries addressed to resellers too.</summary>
    [Fact]
    public void A_customer_and_a_reseller_are_offered_only_the_entries_addressed_to_them()
    {
        var customer = ModuleMenuLocalizer.MenuOf(
            ThreeEntries(),
            new CustomerCaller(Guid.NewGuid(), "acme", Guid.NewGuid()),
            new MarkingTextProvider());
        var reseller = ModuleMenuLocalizer.MenuOf(
            ThreeEntries(),
            new ResellerCaller(Guid.NewGuid(), "reseller", Guid.NewGuid()),
            new MarkingTextProvider());

        Assert.Equal(["reports.home"], customer.Select(item => { return item.Key; }));
        Assert.Equal(["reports.team", "reports.home"], reseller.Select(item => { return item.Key; }));
    }

    /// <summary>Entries with the same order are ordered by key, so the same manifest always yields the same menu.</summary>
    [Fact]
    public void Entries_with_one_order_are_ordered_by_key()
    {
        var manifest = TestManifests.Of(
            "reports",
            menu:
            [
                new ModuleMenuItem("b", "r-b", "chart", "BLabel", 1, ModuleAudience.Everyone),
                new ModuleMenuItem("a", "r-a", "chart", "ALabel", 1, ModuleAudience.Everyone),
            ]);

        var menu = ModuleMenuLocalizer.MenuOf(manifest, AnonymousCaller.Instance, new MarkingTextProvider());

        Assert.Equal(["a", "b"], menu.Select(item => { return item.Key; }));
    }

    /// <summary>A module with no menu and no routes contributes nothing.</summary>
    [Fact]
    public void A_module_with_no_screens_contributes_no_menu_and_no_routes()
    {
        var manifest = TestManifests.Of("quiet");

        Assert.Empty(ModuleMenuLocalizer.MenuOf(manifest, AnonymousCaller.Instance, new MarkingTextProvider()));
        Assert.Empty(ModuleMenuLocalizer.RoutesOf(manifest, new MarkingTextProvider()));
    }
}
