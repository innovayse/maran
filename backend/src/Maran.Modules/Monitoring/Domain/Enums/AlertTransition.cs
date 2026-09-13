
namespace Maran.Modules.Monitoring.Domain.Enums;

/// <summary>What one observation did to an <see cref="Entities.AlertState"/>.</summary>
/// <remarks>
/// This is the whole reason alerts are a state machine rather than a threshold test. A threshold
/// test answers "is the disk full", which is true on every one of the ten samples that crossed it
/// and on every sample after — so a mail per true answer is a mail per minute for as long as the
/// condition lasts. A transition answers "did this become true just now", which happens exactly
/// once per episode, and is what makes ten consecutive breaching samples produce ONE mail.
/// </remarks>
public enum AlertTransition
{
    /// <summary>Nothing changed: still healthy, or still firing. The ordinary outcome, and it sends nothing.</summary>
    None = 0,

    /// <summary>The condition crossed into alarm on this observation. Sends one mail, journals one <c>AlertRaised</c>.</summary>
    Raised = 1,

    /// <summary>The condition returned to normal on this observation. Sends one mail, journals one <c>AlertResolved</c>.</summary>
    Resolved = 2,
}
