namespace Maran.Modules.Ftp.Domain.Enums;

/// <summary>
/// Whether what the panel WANTS (<see cref="FtpsDesiredState"/>) and what the agent OBSERVED on the
/// host tell the same story, and if not, which way they disagree.
/// </summary>
/// <remarks>
/// The rule lives on the backend, in <see cref="Policies.FtpsAgreementPolicy"/>, and reaches the SPA
/// already decided: a screen that derived it from the raw facts would be a second statement of the
/// rule, free to drift from the first. Each disagreement names its direction because each has a
/// different repair — stop the service, turn it on again, or look at what holds the control port.
/// </remarks>
public enum FtpsAgreement
{
    /// <summary>The intention and the observation agree; there is nothing to explain.</summary>
    Consistent = 1,

    /// <summary>
    /// The service is running although the panel does not want it — turned off, or never configured.
    /// A listening daemon the panel reports as off is a door the operator believes is closed.
    /// </summary>
    RunningButOff = 2,

    /// <summary>The panel wants the daemon on and the service manager reports it is not running.</summary>
    OnButNotRunning = 3,

    /// <summary>
    /// The panel wants the daemon on and the service manager reports it active, but the control port
    /// gave no greeting on the host itself, so no client can be served.
    /// </summary>
    RunningButNotAnswering = 4,
}
