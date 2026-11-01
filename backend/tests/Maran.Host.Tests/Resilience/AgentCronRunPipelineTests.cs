using Maran.Host.Resilience;

namespace Maran.Host.Tests.Resilience;

/// <summary>
/// Pins the bound a cron entry run by hand is waited for, against the two numbers it must sit between.
/// </summary>
public sealed class AgentCronRunPipelineTests
{
    /// <summary>The agent's own client deadline for a run by hand (<c>CallClass::CronRun</c>).</summary>
    private static readonly TimeSpan AgentRunDeadline = TimeSpan.FromSeconds(180);

    /// <summary>The panel vhost's <c>proxy_read_timeout</c> (<c>installer/nginx/maran.conf</c>).</summary>
    private static readonly TimeSpan ProxyReadTimeout = TimeSpan.FromSeconds(300);

    /// <summary>The panel waits longer than the agent, so the agent's own answer arrives first.</summary>
    [Fact]
    public void The_panel_waits_longer_than_the_agents_own_deadline()
    {
        Assert.True(AgentCronRunPipeline.Timeout > AgentRunDeadline);
    }

    /// <summary>The panel answers before the reverse proxy gives up on it.</summary>
    [Fact]
    public void The_panel_answers_before_the_reverse_proxy_gives_up()
    {
        Assert.True(AgentCronRunPipeline.Timeout < ProxyReadTimeout);
    }
}
