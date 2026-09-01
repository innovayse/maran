using Maran.Host.Modules;
using Maran.Sdk.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.Composition;

/// <summary>
/// Every module controller, asked whether the container can actually build it — inside a request
/// scope, under the validation a real boot performs.
/// </summary>
/// <remarks>
/// A controller is activated by MVC per request, NOT by the container at startup, so a dependency a
/// module forgot to register is not a boot failure: the panel starts perfectly and the route answers
/// 500 the first time a customer opens the screen. Nothing in a unit suite notices, because a unit
/// test constructs the controller with the doubles it already has in hand.
///
/// The list is read off the module registry by reflection rather than written out, so a controller
/// added later is covered without anybody remembering to add a row — the same reason the IDOR
/// fixtures assert their own completeness instead of trusting it.
/// </remarks>
public sealed class ControllerActivationTests : IClassFixture<ValidatingPanelTestFactory>
{
    /// <summary>The host booted with a real boot's container validation.</summary>
    private readonly ValidatingPanelTestFactory _factory;

    /// <summary>Captures the validating host factory.</summary>
    /// <param name="factory">The booted host.</param>
    public ControllerActivationTests(ValidatingPanelTestFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Every controller every compiled-in module contributes.</summary>
    /// <returns>The controller types, one theory row each.</returns>
    public static TheoryData<Type> ModuleControllers()
    {
        var controllers = ModuleRegistry.All
            .Select(module =>
            {
                return module.GetType().Assembly;
            })
            .Distinct()
            .SelectMany(assembly =>
            {
                return assembly.GetTypes();
            })
            .Where(type =>
            {
                return typeof(BaseApiController).IsAssignableFrom(type) && !type.IsAbstract;
            })
            .OrderBy(type =>
            {
                return type.FullName;
            }, StringComparer.Ordinal);

        var rows = new TheoryData<Type>();
        foreach (var controller in controllers)
        {
            rows.Add(controller);
        }

        return rows;
    }

    /// <summary>The registry contributes at least one controller, so the theory is not empty.</summary>
    [Fact]
    public void The_registry_contributes_at_least_one_controller()
    {
        // A reflection-driven theory that finds nothing passes silently, which is the "no tests
        // found is a failure" rule applied one level down (rules/testing.md).
        Assert.NotEmpty(ModuleControllers());
    }

    /// <summary>Every module controller can be built from a request scope.</summary>
    /// <param name="controllerType">The controller MVC would activate for a request.</param>
    [Theory]
    [MemberData(nameof(ModuleControllers))]
    public void Every_module_controller_can_be_built_from_a_request_scope(Type controllerType)
    {
        using var scope = _factory.Services.CreateScope();

        var controller = ActivatorUtilities.CreateInstance(scope.ServiceProvider, controllerType);

        Assert.NotNull(controller);
    }
}
