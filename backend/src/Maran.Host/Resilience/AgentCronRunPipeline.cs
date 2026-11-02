using Polly;

namespace Maran.Host.Resilience;

/// <summary>
/// The pipeline a cron entry run by hand is called through (issue #158): the operation pipeline's
/// retry, under a timeout long enough for the run itself.
/// </summary>
/// <remarks>
/// <para>
/// Its own pipeline because the run is slow BY DESIGN. The agent waits for the customer's job,
/// which the service manager stops at two minutes (<c>RUN_NOW_RUNTIME_CEILING</c> in the agent's
/// <c>ops::cron</c>), and the agent's own client deadline is three minutes
/// (<c>CallClass::CronRun</c>). Under <see cref="AgentOperationPipeline"/>'s thirty seconds the panel
/// would report the agent unavailable while the job was still running, and the job would finish
/// with nobody listening.
/// </para>
/// <para>
/// <see cref="Timeout"/> sits above the agent's deadline, so the agent's own answer arrives first,
/// and below the panel vhost's <c>proxy_read_timeout 300s</c> (<c>installer/nginx/maran.conf</c>), so
/// the browser hears the panel's answer rather than the reverse proxy's 504. The three numbers move
/// together.
/// </para>
/// <para>
/// The retry is the operation pipeline's, unchanged: only a call that could not be made is
/// repeated, so a run that started is never started twice by this pipeline.
/// </para>
/// </remarks>
public static class AgentCronRunPipeline
{
    /// <summary>The registry key the pipeline is resolved by.</summary>
    public const string Name = "agent-cron-run";

    /// <summary>How long one run-now call may take before it is answered as unavailable.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(200);

    /// <summary>Configures the pipeline on <paramref name="builder"/>.</summary>
    /// <param name="builder">The pipeline being built.</param>
    public static void Configure(ResiliencePipelineBuilder builder)
    {
        AgentOperationPipeline.Configure(builder, Timeout);
    }
}
