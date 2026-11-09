using System.Reflection;
using Maran.Host.Demo.Guard;
using Maran.Host.Demo.Guard.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Maran.Host.Demo.Tests.Fixtures;

/// <summary>Builds the endpoints and the table the guard tests judge.</summary>
public static class DemoEndpoints
{
    /// <summary>A table over <see cref="PlantedDemoController"/> that leaves one action unnamed.</summary>
    public static DemoEndpointPolicy PlantedPolicy { get; } = new(
        new Dictionary<string, DemoEndpointRule>(StringComparer.Ordinal)
        {
            ["PlantedDemoController.RefusedAsync"] = DemoEndpointRule.Refuse(DemoRefusalArea.Smtp),
            ["PlantedDemoController.DeleteAsync"] = DemoEndpointRule.AllowUnlessProtected("id"),
            ["PlantedDemoController.OpenAsync"] = DemoEndpointRule.AllowUncounted(),
            ["PlantedDemoController.CountedAsync"] = DemoEndpointRule.Allow(),
            ["PlantedDemoController.SignInAsync"] = DemoEndpointRule.AllowSignIn(),
        });

    /// <summary>An endpoint whose metadata carries a controller action descriptor, as MVC builds them.</summary>
    /// <param name="controller">The controller type.</param>
    /// <param name="action">The action's method name.</param>
    /// <returns>The endpoint.</returns>
    public static Endpoint ForAction(Type controller, string action)
    {
        var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance)!;
        var descriptor = new ControllerActionDescriptor
        {
            MethodInfo = method,
            ControllerTypeInfo = controller.GetTypeInfo(),
            ActionName = action,
            ControllerName = controller.Name,
        };
        return new Endpoint(_ => { return Task.CompletedTask; }, new EndpointMetadataCollection(descriptor), action);
    }

    /// <summary>A minimal endpoint, which carries no controller descriptor.</summary>
    /// <param name="pattern">The route pattern it was mapped with.</param>
    /// <returns>The endpoint.</returns>
    public static Endpoint Minimal(string pattern)
    {
        return new RouteEndpoint(
            _ => { return Task.CompletedTask; },
            RoutePatternFactory.Parse(pattern),
            0,
            EndpointMetadataCollection.Empty,
            pattern);
    }
}
