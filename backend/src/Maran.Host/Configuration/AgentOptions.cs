using System.ComponentModel.DataAnnotations;

namespace Maran.Host.Configuration;

/// <summary>
/// Settings for reaching the local agent over its unix domain socket. Bound from the
/// <c>Agent</c> configuration section and validated at startup, so a misconfigured probe
/// timeout or an empty socket path fails the boot rather than the first health check.
/// </summary>
public sealed class AgentOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "Agent";

    /// <summary>Filesystem path to the agent's unix domain socket.</summary>
    [Required]
    [MinLength(1)]
    public string SocketPath { get; set; } = "/run/maran/agent.sock";

    /// <summary>
    /// How long the agent handshake may take before a health probe reports it unavailable, in
    /// seconds. Deliberately short: health must answer quickly even when the agent socket is
    /// missing or unresponsive.
    /// </summary>
    [Range(1, 30)]
    public int ProbeTimeoutSeconds { get; set; } = 2;

    /// <summary>
    /// How long an account OPERATION may take before it is abandoned. Far longer than
    /// <see cref="ProbeTimeoutSeconds"/> on purpose: a probe asks a question and a slow answer is
    /// itself the answer, while <c>useradd</c> on a busy host creating a home directory is simply
    /// slow, and cutting it off leaves work half done for no benefit.
    /// </summary>
    public int OperationTimeoutSeconds { get; set; } = 30;

    /// <summary><see cref="OperationTimeoutSeconds"/> as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan OperationTimeout
    {
        get
        {
            return TimeSpan.FromSeconds(OperationTimeoutSeconds);
        }
    }

    /// <summary><see cref="ProbeTimeoutSeconds"/> as a <see cref="TimeSpan"/>, for callers that need one.</summary>
    public TimeSpan ProbeTimeout
    {
        get
        {
            return TimeSpan.FromSeconds(ProbeTimeoutSeconds);
        }
    }
}
