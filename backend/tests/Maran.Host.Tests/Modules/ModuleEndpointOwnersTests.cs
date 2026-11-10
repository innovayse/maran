using System.Reflection;
using Maran.Host.Modules;
using Maran.Host.Tests.Modules.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Maran.Host.Tests.Modules;

/// <summary>
/// Behavioral contract of <see cref="ModuleEndpointOwners"/>: an endpoint belongs to the module whose
/// assembly declares its controller, and to nobody when no controller serves it or the assembly cannot be
/// attributed to one module.
/// </summary>
public sealed class ModuleEndpointOwnersTests
{
    /// <summary>The map built from the shipped modules is not empty - the guard on the axis that can go blind.</summary>
    [Fact]
    public void The_map_built_from_the_shipped_modules_owns_assemblies()
    {
        var owners = new ModuleEndpointOwners(ModuleRegistry.All);

        Assert.True(owners.OwnedAssemblyCount > 0);
    }

    /// <summary>Every shipped module that declares a controller owns that controller's endpoints.</summary>
    [Fact]
    public void Every_shipped_module_with_a_controller_owns_it()
    {
        var owners = new ModuleEndpointOwners(ModuleRegistry.All);
        var examined = 0;

        foreach (var module in ModuleRegistry.All)
        {
            var controller = module.GetType().Assembly.GetTypes()
                .FirstOrDefault(type => { return !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type); });
            if (controller is null)
            {
                continue;
            }

            examined++;
            Assert.Equal(module.Manifest.Id, owners.OwnerOf(EndpointOf(controller.GetTypeInfo())));
        }

        Assert.True(examined > 0, "No shipped module declared a controller, so nothing was judged.");
    }

    /// <summary>An endpoint served by no controller has no owner.</summary>
    [Fact]
    public void An_endpoint_no_controller_serves_has_no_owner()
    {
        var owners = new ModuleEndpointOwners(ModuleRegistry.All);
        var endpoint = new Endpoint(_ =>
        {
            return Task.CompletedTask;
        }, new EndpointMetadataCollection(), "minimal");

        Assert.Null(owners.OwnerOf(endpoint));
    }

    /// <summary>A controller from an assembly no composed module lives in has no owner.</summary>
    [Fact]
    public void A_controller_from_an_assembly_no_composed_module_lives_in_has_no_owner()
    {
        var owners = new ModuleEndpointOwners(ModuleRegistry.All);

        Assert.Null(owners.OwnerOf(EndpointOf(typeof(ModuleEndpointOwnersTests).GetTypeInfo())));
    }

    /// <summary>Two composed modules in one assembly leave that assembly without an owner rather than guess one.</summary>
    [Fact]
    public void An_assembly_two_composed_modules_share_has_no_owner()
    {
        var owners = new ModuleEndpointOwners(
        [
            new StubPackagedModule(TestManifests.Of("alpha")),
            new StubPackagedModule(TestManifests.Of("beta")),
        ]);

        Assert.Equal(0, owners.OwnedAssemblyCount);
        Assert.Null(owners.OwnerOf(EndpointOf(typeof(ModuleEndpointOwnersTests).GetTypeInfo())));
    }

    /// <summary>One composed module in an assembly owns its controllers - the inverse of the shared-assembly refusal.</summary>
    [Fact]
    public void An_assembly_one_composed_module_lives_in_is_owned_by_it()
    {
        var owners = new ModuleEndpointOwners([new StubPackagedModule(TestManifests.Of("alpha"))]);

        Assert.Equal("alpha", owners.OwnerOf(EndpointOf(typeof(ModuleEndpointOwnersTests).GetTypeInfo())));
    }

    /// <summary>Builds an endpoint the way routing describes one served by a controller.</summary>
    /// <param name="controller">The controller type that serves it.</param>
    /// <returns>The endpoint.</returns>
    private static Endpoint EndpointOf(TypeInfo controller)
    {
        var descriptor = new ControllerActionDescriptor { ControllerTypeInfo = controller };

        return new Endpoint(_ =>
        {
            return Task.CompletedTask;
        }, new EndpointMetadataCollection(descriptor), "controller");
    }
}
