using System.Net;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SystemService;
using Maran.Host.BackgroundServices;
using Maran.Host.Tests.HealthChecks;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Maran.Host.Tests.Composition;

/// <summary>
/// The startup check as the composed host runs it: an agent whose contract the panel does not drive is
/// logged and the panel keeps running, because a host that stopped over it would be restarted by its
/// service manager into the same refusal.
/// </summary>
public sealed class AgentStartupCheckCompositionTests : IClassFixture<PanelTestFactory>
{
    /// <summary>Deadline for the check's background run, so a stuck one fails instead of hanging.</summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The shared in-memory host factory.</summary>
    private readonly PanelTestFactory _factory;

    /// <summary>Captures the shared in-memory host factory.</summary>
    /// <param name="factory">The booted host.</param>
    public AgentStartupCheckCompositionTests(PanelTestFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The startup check is one of the host's hosted services.</summary>
    [Fact]
    public void The_startup_check_is_registered_as_a_hosted_service()
    {
        var hosted = _factory.Services.GetServices<IHostedService>();

        Assert.Single(hosted.OfType<AgentCompatibilityStartupCheck>());
    }

    /// <summary>
    /// The host keeps running after its startup check finds an agent outside the window: the host was
    /// not asked to stop and its liveness endpoint still answers.
    /// </summary>
    [Fact]
    public async Task The_host_keeps_running_after_its_startup_check_finds_an_incompatible_agent()
    {
        var refused = new StubAgentSystemClient(
            Result<AgentInfoDto>.Fail(Error.Of(AgentProtoVersionWindow.IncompatibleCode, ErrorType.Unavailable)));

        await AssertHostKeepsRunningAsync(refused);
    }

    /// <summary>
    /// The host keeps running even when the probe under the startup check hits a defect, because the
    /// check's own guard keeps it from faulting the background service, whose default is to stop the host.
    /// </summary>
    [Fact]
    public async Task The_host_keeps_running_after_a_defect_inside_its_startup_check()
    {
        var defect = new StubAgentSystemClient(new InvalidOperationException("a defect inside the probe"));

        await AssertHostKeepsRunningAsync(defect);
    }

    /// <summary>
    /// Boots the host with the given agent client, waits for the startup check to finish, and asserts the
    /// host was not told to stop and still answers liveness.
    /// </summary>
    /// <param name="agent">The client standing in for the agent.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    private async Task AssertHostKeepsRunningAsync(IAgentSystemClient agent)
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(agent);
            });
        });
        using var client = factory.CreateClient();

        var check = factory.Services.GetServices<IHostedService>().OfType<AgentCompatibilityStartupCheck>().Single();
        var executing = check.ExecuteTask!;
        await executing.WaitAsync(CheckTimeout);

        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);
        Assert.True(executing.IsCompletedSuccessfully);

        var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }
}
