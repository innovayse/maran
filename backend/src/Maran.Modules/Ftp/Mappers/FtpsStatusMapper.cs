using Maran.Modules.Ftp.Common;
using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Domain.Policies;
using AgentFtpsStatus = Maran.Agent.Client.Services.FtpsService.FtpsStatusDto;

namespace Maran.Modules.Ftp.Mappers;

/// <summary>
/// Restates the agent's observation of this server's FTPS daemon as the screen's
/// <see cref="FtpsStatusDto"/>, adding the two facts the agent has no way to know: the hostname the
/// panel persisted, and the control port the panel chose.
/// </summary>
/// <remarks>
/// <para>
/// A mapper translates and never decides (rules/csharp.md). Every daemon field below is copied
/// across unchanged, in particular <see cref="AgentFtpsStatus.ForcedTls"/>: it is not defaulted, not
/// inverted, and not replaced by the panel's own intention when the agent reports <c>false</c>.
/// Substituting an intention there is the one edit that would make the record lie in the direction
/// that matters — a green screen over a daemon that takes passwords in the clear.
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
    /// <param name="settings">
    /// The persisted settings row, or <c>null</c> when FTPS has never been enabled here. A null row
    /// yields a null <c>Hostname</c> and <c>Enabled: false</c> — an absence, stated as one.
    /// </param>
    /// <returns>The status an administrator's screen renders.</returns>
    public static FtpsStatusDto ToDto(AgentFtpsStatus observed, FtpsSettings? settings)
    {
        return new FtpsStatusDto(
            settings?.Hostname,
            settings?.Enabled ?? false,
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
