using Maran.Host.Modules;
using Maran.Host.Modules.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Host.Tests.Modules;

/// <summary>Behavioral contract of <see cref="ModuleStateResolver"/>, the one decision point for a module's state.</summary>
public sealed class ModuleStateResolverTests
{
    /// <summary>A module that asks for no licence is enabled.</summary>
    [Fact]
    public void An_included_module_is_enabled()
    {
        Assert.Equal(ModuleState.Enabled, ModuleStateResolver.Of(LicenceTier.Included));
    }

    /// <summary>A module sold as an add-on is locked while no installed licence can cover it.</summary>
    [Fact]
    public void An_add_on_module_is_locked_while_no_licence_can_cover_it()
    {
        Assert.Equal(ModuleState.Locked, ModuleStateResolver.Of(LicenceTier.AddOn));
    }

    /// <summary>A module gated by the panel plan is locked while no installed licence can cover it.</summary>
    [Fact]
    public void A_plan_gated_module_is_locked_while_no_licence_can_cover_it()
    {
        Assert.Equal(ModuleState.Locked, ModuleStateResolver.Of(LicenceTier.PlanGated));
    }

    /// <summary>A tier outside the enum is refused rather than guessed at.</summary>
    [Fact]
    public void A_tier_outside_the_enum_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return ModuleStateResolver.Of((LicenceTier)99);
        });
    }
}
