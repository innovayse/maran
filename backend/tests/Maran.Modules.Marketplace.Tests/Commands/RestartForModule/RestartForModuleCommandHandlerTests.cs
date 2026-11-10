using Maran.Modules.Marketplace.Commands.RestartForModule;
using Maran.Modules.Marketplace.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Marketplace.Tests.Commands.RestartForModule;

/// <summary>A restart is requested through the Sdk's restart seam for a module the loader lists, and for no other.</summary>
public sealed class RestartForModuleCommandHandlerTests
{
    /// <summary>A module the loader lists asks the panel to restart for it, once per request.</summary>
    [Fact]
    public async Task A_listed_module_asks_the_panel_to_restart()
    {
        using var world = new OperationWorld();
        world.Loader.Seed("mail", "1.0.0", ModuleLifecycleState.Installed);
        var restart = new RecordingPanelRestart();

        var result = await new RestartForModuleCommandHandler(world.Installed, restart)
            .HandleAsync(new RestartForModuleCommand("mail"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("mail", Assert.Single(restart.Requested));
    }

    /// <summary>A module the loader does not list is not found and nothing restarts.</summary>
    [Fact]
    public async Task An_unknown_module_is_not_found_and_nothing_restarts()
    {
        using var world = new OperationWorld();
        var restart = new RecordingPanelRestart();

        var result = await new RestartForModuleCommandHandler(world.Installed, restart)
            .HandleAsync(new RestartForModuleCommand("ghost"), CancellationToken.None);

        Assert.Equal(ErrorCodes.ModuleNotInstalled, result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(restart.Requested);
    }
}
