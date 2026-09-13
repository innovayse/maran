using Maran.Host.Modules;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.Composition;

/// <summary>
/// Proves the Ftp module is actually part of the running panel, on the two axes that can fail
/// independently: it is in the registry, and what it contributes is reachable.
/// </summary>
/// <remarks>
/// <para>
/// The two axes are separate because the two halves of composition are separate files. The
/// registry entry in <see cref="ModuleRegistry"/> is what gives the module its DI registrations,
/// its Wolverine handler assembly, its manifest row in <c>GET /api/v1/modules</c> and its place in
/// the account-residue audit. The <c>ProjectReference</c> in <c>Maran.Host.csproj</c> is what makes
/// the module's assembly an MVC application part, which is what puts its controllers in the route
/// table. A module can hold one without the other: dropping the application part leaves every
/// service registered, every handler discovered and every manifest row published while every route
/// the module declares answers 404 — a panel that reports the module as installed and serves none
/// of it.
/// </para>
/// <para>
/// Neither test enumerates a written-down list of modules or routes. The first reads the registry
/// itself, so removing the entry is what makes it fail rather than forgetting to update a second
/// copy. The second derives the routes from the module's own assembly, so it cannot pass by finding
/// nothing: an empty census is the shape a missing module takes, and it is asserted against
/// directly.
/// </para>
/// </remarks>
public sealed class FtpModuleCompositionTests : IClassFixture<ValidatingPanelTestFactory>
{
    /// <summary>The module id the Ftp module publishes in its manifest.</summary>
    private const string FtpModuleId = "ftp";

    /// <summary>The host booted with a real boot's container validation.</summary>
    private readonly ValidatingPanelTestFactory _factory;

    /// <summary>Captures the validating host factory.</summary>
    /// <param name="factory">The booted host.</param>
    public FtpModuleCompositionTests(ValidatingPanelTestFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The Ftp module is one of the modules the panel composes.</summary>
    [Fact]
    public void The_ftp_module_is_composed_into_the_panels_module_registry()
    {
        var composed = ModuleRegistry.All
            .Select(module =>
            {
                return module.Manifest.Id;
            })
            .ToList();

        Assert.Contains(
            FtpModuleId,
            composed);
    }

    /// <summary>Every route the Ftp module declares is in the booted panel's route table.</summary>
    [Fact]
    public void Every_route_the_ftp_module_declares_is_mapped_by_the_booted_panel()
    {
        var declared = DeclaredRouteTemplates(FtpModuleId);

        // The vacuity guard, on the axis that goes blind: a module that is not composed contributes
        // no controllers, so the comparison below would hold over an empty census and report the
        // absence as a pass. This is the line that refuses that reading.
        Assert.NotEmpty(declared);

        var mapped = _factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint =>
            {
                return endpoint.RoutePattern.RawText ?? string.Empty;
            })
            .ToList();

        var unmapped = declared
            .Where(template =>
            {
                return !mapped.Any(pattern =>
                {
                    return pattern.StartsWith(template, StringComparison.Ordinal);
                });
            })
            .ToList();

        Assert.True(
            unmapped.Count == 0,
            $"The '{FtpModuleId}' module is registered but these routes it declares are in no "
            + $"endpoint of the booted panel, so the module is composed and unreachable: "
            + $"{string.Join(", ", unmapped)}.");
    }

    /// <summary>
    /// Reads the route templates a composed module's controllers declare, from the module's own
    /// assembly rather than from a list written here.
    /// </summary>
    /// <param name="moduleId">The manifest id of the module to read.</param>
    /// <returns>One template per controller, empty when the module is not composed at all.</returns>
    private static List<string> DeclaredRouteTemplates(string moduleId)
    {
        return ModuleRegistry.All
            .Where(module =>
            {
                return string.Equals(module.Manifest.Id, moduleId, StringComparison.Ordinal);
            })
            .SelectMany(module =>
            {
                return module.GetType().Assembly.GetTypes();
            })
            .Where(type =>
            {
                return typeof(BaseApiController).IsAssignableFrom(type) && !type.IsAbstract;
            })
            .Select(type =>
            {
                return type.GetCustomAttributes(typeof(RouteAttribute), inherit: false)
                    .OfType<RouteAttribute>()
                    .Select(route =>
                    {
                        return route.Template;
                    })
                    .FirstOrDefault();
            })
            .Where(template =>
            {
                return !string.IsNullOrWhiteSpace(template);
            })
            .Select(template =>
            {
                return template!;
            })
            .ToList();
    }
}
