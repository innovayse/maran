using Maran.Modules.Ftp.Domain.Enums;

namespace Maran.Modules.Ftp.Domain.Policies;

/// <summary>
/// The one statement of when the panel's intention and the agent's observation of the FTPS daemon
/// disagree, and in which direction.
/// </summary>
/// <remarks>
/// <para>
/// A stateless rule over values, so <c>Domain/Policies/</c>. It is not written inside
/// <c>FtpsStatusMapper</c>, which calls it: a mapper restates and never decides (rules/csharp.md), and
/// this is a decision with a precedence that has to be read in one place.
/// </para>
/// <para>
/// <b>The precedence, and why.</b> A running service the panel does not want is checked first,
/// because it is the one disagreement with a security consequence — a listening daemon on a screen
/// that says "off" — and its repair (stop the service) is the same whether or not the port answered.
/// A wanted daemon that is not running comes next. Only a running, wanted daemon is asked whether its
/// control port greeted, because the probe of a stopped daemon is expected to fail and says nothing.
/// </para>
/// </remarks>
public static class FtpsAgreementPolicy
{
    /// <summary>Decides how the intention and the observation relate.</summary>
    /// <param name="desired">What the panel wants.</param>
    /// <param name="running">The service manager reports the FTPS unit active.</param>
    /// <param name="controlPortAnswered">The control port greeted a connect made on the host.</param>
    /// <returns>The agreement, or the direction of the disagreement.</returns>
    public static FtpsAgreement Of(FtpsDesiredState desired, bool running, bool controlPortAnswered)
    {
        if (running && desired != FtpsDesiredState.On)
        {
            return FtpsAgreement.RunningButOff;
        }

        if (desired == FtpsDesiredState.On && !running)
        {
            return FtpsAgreement.OnButNotRunning;
        }

        if (running && !controlPortAnswered)
        {
            return FtpsAgreement.RunningButNotAnswering;
        }

        return FtpsAgreement.Consistent;
    }
}
