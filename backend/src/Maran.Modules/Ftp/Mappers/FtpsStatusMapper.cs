using Maran.Modules.Ftp.Common;
using Maran.Modules.Ftp.Domain.Enums;
using Maran.Modules.Ftp.Domain.Policies;
using AgentFtpsStatus = Maran.Agent.Client.Services.FtpsService.FtpsStatusDto;

namespace Maran.Modules.Ftp.Mappers;

/// <summary>
/// Restates the agent's observation of this server's FTPS daemon as the screen's
/// <see cref="FtpsStatusDto"/>, adding the facts the agent has no way to know: the hostname the
/// panel persisted, what the panel wants, and the control port the panel chose.
/// </summary>
/// <remarks>
/// <para>
/// A mapper translates and never decides (rules/csharp.md). The desired state arrives already
/// decided by the handler that read the row (<see cref="FtpsDesiredStatePolicy"/>), and the
/// agreement is <see cref="FtpsAgreementPolicy"/>'s verdict, restated here rather than reasoned out.
/// Every daemon field is copied across unchanged, in particular
/// <see cref="AgentFtpsStatus.ForcedTls"/>: it is not defaulted, not inverted, and not replaced by
/// the panel's own intention when the agent reports <c>false</c>. Substituting an intention there is
/// the one edit that would make the record lie in the direction that matters — a green screen over a
/// daemon that takes passwords in the clear.
/// </para>
/// <para>
/// The alias on the import is not decoration: both the agent's observation and this module's screen
/// shape are called <c>FtpsStatusDto</c>, and naming the same thing the same way in two projects is
/// the convention (rules/csharp.md, "Two modules may hold a same-named file"). One file has to see
/// both, and an alias states which is which at the point of use instead of leaving a reader to
/// count namespaces.
/// </para>
/// </remarks>
public static class FtpsStatusMapper
{
    /// <summary>Builds the screen's status from what the agent observed and what the panel holds.</summary>
    /// <param name="observed">The agent's measurement of the daemon on this host.</param>
    /// <param name="hostname">
    /// The hostname the panel persisted, or <c>null</c> when it never persisted one — an absent value,
    /// not a state: the state is <paramref name="desired"/>.
    /// </param>
    /// <param name="desired">What the panel wants, already decided from its settings row.</param>
    /// <returns>The status an administrator's screen renders.</returns>
    public static FtpsStatusDto ToDto(AgentFtpsStatus observed, string? hostname, FtpsDesiredState desired)
    {
        return new FtpsStatusDto(
            hostname,
            desired,
            FtpsAgreementPolicy.Of(desired, observed.Running, observed.ControlPortAnswered),
            observed.Running,
            observed.ControlPortAnswered,
            observed.ForcedTls,
            observed.CertificatePresent,
            observed.CertificateIsSelfSigned,
            observed.CertificatePath,
            FtpsDefaults.ControlPort,
            observed.PassivePortMin,
            observed.PassivePortMax,
            observed.Ipv4Only);
    }
}
