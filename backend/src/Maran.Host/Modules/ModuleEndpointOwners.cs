using System.Reflection;
using Maran.Sdk.Interfaces;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Maran.Host.Modules;

/// <summary>
/// Which module owns an endpoint: the module whose assembly declares the controller that serves it. Built
/// once at start from the composed modules, the only place that knows them all, and read by
/// <see cref="Middleware.ModuleFrameScopeMiddleware"/> to hold a framed call to its own module's endpoints.
/// </summary>
/// <remarks>
/// An endpoint no controller serves (the catalogue, health, search - the Host's own minimal endpoints) has
/// no owner, and neither has a controller in an assembly that two composed modules share: ownership that
/// cannot be told is not guessed. A scope check treats both as out of scope.
/// </remarks>
public sealed class ModuleEndpointOwners
{
    /// <summary>The module id for each assembly that exactly one composed module lives in.</summary>
    private readonly Dictionary<Assembly, string> _ownerByAssembly;

    /// <summary>How many assemblies have an owner; zero would mean the map was built from nothing.</summary>
    public int OwnedAssemblyCount
    {
        get { return _ownerByAssembly.Count; }
    }

    /// <summary>Builds the map from the composed modules.</summary>
    /// <param name="modules">Every composed module, shipped and packaged.</param>
    public ModuleEndpointOwners(IReadOnlyList<IPanelModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        _ownerByAssembly = modules
            .GroupBy(module => { return module.GetType().Assembly; })
            .Where(group => { return group.Count() == 1; })
            .ToDictionary(group => { return group.Key; }, group => { return group.Single().Manifest.Id; });
    }

    /// <summary>Finds the module that owns an endpoint.</summary>
    /// <param name="endpoint">The endpoint the request matched.</param>
    /// <returns>The owning module's id, or null when no composed module owns it.</returns>
    public string? OwnerOf(Endpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var controller = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo;

        return controller is not null && _ownerByAssembly.TryGetValue(controller.Assembly, out var owner)
            ? owner
            : null;
    }
}
