using Maran.Host.Modules;
using Maran.Sdk.Contracts;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Mvc;

namespace Maran.ArchitectureTests;

/// <summary>
/// Every controller of a module that is not <see cref="LicenceTier.Included"/> carries
/// <c>[RequiresLicensedModule("&lt;its module id&gt;")]</c>, so the licence is checked on every request
/// to a paid module and not only by what the menu shows (docs/superpowers/notes/2026-11-07-licensing-lease-threat-note.md,
/// reviewer item 1).
/// </summary>
/// <remarks>
/// <para>
/// <b>The census is vacuous on today's tree, and says so.</b> Every module the panel ships declares
/// <c>Included</c>, so the walk over the real composed modules finds no controller that needs the attribute.
/// A check that passes by finding nothing is exactly the blind gate rules/testing.md names, so the walk is
/// proven on a PLANTED paid module: one controller that declares the attribute for its module (accepted), one
/// that declares it for another module (refused), and one that declares nothing (refused). The check also
/// counts how many controllers it examined on the real tree, so a walk that silently saw none fails.
/// </para>
/// <para>
/// UNOBSERVED HERE: a paid module's background job calling the licence window. There is no mechanical check
/// for that; it is a review item.
/// </para>
/// </remarks>
public sealed class LicensedModuleDeclarationTests
{
    /// <summary>No controller of a paid module on the real composed tree lacks the attribute for its module.</summary>
    [Fact]
    public void Every_controller_of_a_paid_module_declares_the_licence_gate_for_its_module()
    {
        var offenders = Undeclared(RealModules());

        Assert.True(
            offenders.Count == 0,
            "Controllers of a paid module without [RequiresLicensedModule(<module id>)]: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// The vacuity guard, on the axis that can go blind: the walk reaches controllers at all (it examines the
    /// included modules' too, which need no attribute), so "no offenders" is not the answer of an empty walk.
    /// </summary>
    [Fact]
    public void The_walk_reaches_the_controllers_of_the_composed_modules()
    {
        var examined = RealModules().Sum(module => { return module.Controllers.Count; });

        Assert.True(examined > 20, $"The walk examined only {examined} controllers; it has gone blind.");
    }

    /// <summary>The planted violations are found, and the declared controller beside them is accepted.</summary>
    [Fact]
    public void A_planted_paid_module_with_an_undeclared_controller_is_refused_and_the_declared_one_is_accepted()
    {
        var planted = new ModuleControllers(
            "plantedmodule",
            LicenceTier.AddOn,
            [typeof(DeclaredController), typeof(WrongModuleController), typeof(UndeclaredController)]);

        var offenders = Undeclared([planted]);

        Assert.Equal(
            [nameof(UndeclaredController), nameof(WrongModuleController)],
            offenders.Select(offender => { return offender.Split('.', '+').Last(); }).Order(StringComparer.Ordinal));
    }

    /// <summary>An included module's undeclared controller is not an offender: the control for the paid-module rule.</summary>
    [Fact]
    public void An_included_module_needs_no_declaration()
    {
        var included = new ModuleControllers("plantedincluded", LicenceTier.Included, [typeof(UndeclaredController)]);

        Assert.Empty(Undeclared([included]));
    }

    /// <summary>Reads every composed module's controllers, grouped by the module whose assembly holds them.</summary>
    /// <returns>One row per composed module.</returns>
    private static List<ModuleControllers> RealModules()
    {
        return ModuleRegistry.All
            .Select(module =>
            {
                var controllers = module.GetType().Assembly.GetTypes()
                    .Where(type => { return typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract; })
                    .ToList();

                return new ModuleControllers(module.Manifest.Id, module.Manifest.Tier, controllers);
            })
            .ToList();
    }

    /// <summary>Names the controllers of paid modules that do not declare the gate for their own module.</summary>
    /// <param name="modules">The modules and their controllers.</param>
    /// <returns>The offending controllers' full names, ordered.</returns>
    private static List<string> Undeclared(IEnumerable<ModuleControllers> modules)
    {
        return modules
            .Where(module => { return module.Tier != LicenceTier.Included; })
            .SelectMany(module =>
            {
                return module.Controllers
                    .Where(controller =>
                    {
                        return !controller.GetCustomAttributes(inherit: false)
                            .OfType<RequiresLicensedModuleAttribute>()
                            .Any(attribute => { return string.Equals(attribute.ModuleId, module.Id, StringComparison.Ordinal); });
                    })
                    .Select(controller => { return controller.FullName!; });
            })
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>One module and the controllers its assembly holds.</summary>
    /// <param name="Id">The module's manifest id.</param>
    /// <param name="Tier">The tier its manifest declares.</param>
    /// <param name="Controllers">Its concrete controllers.</param>
    private sealed record ModuleControllers(string Id, LicenceTier Tier, IReadOnlyList<Type> Controllers);

    /// <summary>A planted controller that declares the gate for its own module.</summary>
    [RequiresLicensedModule("plantedmodule")]
    private sealed class DeclaredController : ControllerBase
    {
    }

    /// <summary>A planted controller that declares the gate for a different module: that gates nothing it should.</summary>
    [RequiresLicensedModule("someothermodule")]
    private sealed class WrongModuleController : ControllerBase
    {
    }

    /// <summary>A planted controller that declares nothing.</summary>
    private sealed class UndeclaredController : ControllerBase
    {
    }
}
