using Maran.Modules.Ftp.Domain.Enums;
using Maran.Modules.Ftp.Mappers;
using AgentFtpsCertificate = Maran.Agent.Client.Services.FtpsService.FtpsCertificate;
using AgentFtpsTlsEnforcement = Maran.Agent.Client.Services.FtpsService.FtpsTlsEnforcement;
using AgentFtpsStatus = Maran.Agent.Client.Services.FtpsService.FtpsStatusDto;

namespace Maran.Modules.Ftp.Tests.Mappers;

/// <summary>Behaviour of the mapper that turns the agent's observation into the screen's status.</summary>
public sealed class FtpsStatusMapperTests
{
    /// <summary>
    /// Every combination of what the panel wants and what the agent observed: three desired states
    /// by running yes/no by control port answered yes/no — twelve rows, the whole space.
    /// </summary>
    /// <remarks>
    /// The whole space and not the interesting corners, because the precedence is the rule: a
    /// running daemon the panel does not want is <c>RunningButOff</c> whether or not its port
    /// answered, and a stopped daemon's failed probe is expected and says nothing. A table that
    /// skipped a row would let a reordering of the policy's branches through unseen.
    /// </remarks>
    public static TheoryData<FtpsDesiredState, bool, bool, FtpsAgreement> EveryCombination
    {
        get
        {
            return new TheoryData<FtpsDesiredState, bool, bool, FtpsAgreement>
            {
                { FtpsDesiredState.NeverConfigured, false, false, FtpsAgreement.Consistent },
                { FtpsDesiredState.NeverConfigured, false, true, FtpsAgreement.Consistent },
                { FtpsDesiredState.NeverConfigured, true, false, FtpsAgreement.RunningButOff },
                { FtpsDesiredState.NeverConfigured, true, true, FtpsAgreement.RunningButOff },
                { FtpsDesiredState.Off, false, false, FtpsAgreement.Consistent },
                { FtpsDesiredState.Off, false, true, FtpsAgreement.Consistent },
                { FtpsDesiredState.Off, true, false, FtpsAgreement.RunningButOff },
                { FtpsDesiredState.Off, true, true, FtpsAgreement.RunningButOff },
                { FtpsDesiredState.On, false, false, FtpsAgreement.OnButNotRunning },
                { FtpsDesiredState.On, false, true, FtpsAgreement.OnButNotRunning },
                { FtpsDesiredState.On, true, false, FtpsAgreement.RunningButNotAnswering },
                { FtpsDesiredState.On, true, true, FtpsAgreement.Consistent },
            };
        }
    }

    /// <summary>The agreement follows the desired state and the two observed facts, for every combination.</summary>
    /// <param name="desired">What the panel wants.</param>
    /// <param name="running">Whether the agent reports the unit active.</param>
    /// <param name="answered">Whether the control port greeted on the host.</param>
    /// <param name="expected">The agreement the screen must be told.</param>
    [Theory]
    [MemberData(nameof(EveryCombination))]
    public void The_agreement_follows_the_desired_state_and_the_observed_facts(
        FtpsDesiredState desired,
        bool running,
        bool answered,
        FtpsAgreement expected)
    {
        var observed = Observation(running, answered);

        var status = FtpsStatusMapper.ToDto(observed, "ftp.example.test", desired);

        Assert.Equal(expected, status.Agreement);
        Assert.Equal(desired, status.DesiredState);
        Assert.Equal(running, status.Running);
        Assert.Equal(answered, status.ControlPortAnswered);
    }

    /// <summary>A server nobody configured is not reported as one an administrator turned off.</summary>
    /// <remarks>
    /// The defect the three-valued state exists to remove: a missing row used to become
    /// <c>Enabled: false</c>, the same answer a deliberate disable gives.
    /// </remarks>
    [Fact]
    public void A_server_nobody_configured_is_not_reported_as_turned_off()
    {
        var status = FtpsStatusMapper.ToDto(
            Observation(running: false, answered: false), null, FtpsDesiredState.NeverConfigured);

        Assert.Equal(FtpsDesiredState.NeverConfigured, status.DesiredState);
        Assert.NotEqual(FtpsDesiredState.Off, status.DesiredState);
        Assert.Null(status.Hostname);
    }

    /// <summary>Every certificate outcome the agent client reports reaches the screen as the same outcome.</summary>
    /// <param name="observed">What the agent client reports.</param>
    /// <param name="expected">What the screen is told.</param>
    [Theory]
    [InlineData(AgentFtpsCertificate.Absent, FtpsCertificateState.Absent)]
    [InlineData(AgentFtpsCertificate.SelfSigned, FtpsCertificateState.SelfSigned)]
    [InlineData(AgentFtpsCertificate.Provided, FtpsCertificateState.Provided)]
    public void Every_certificate_outcome_reaches_the_screen_as_the_same_outcome(
        AgentFtpsCertificate observed,
        FtpsCertificateState expected)
    {
        var status = FtpsStatusMapper.ToDto(
            Observation(running: true, answered: true, observed), "ftp.example.test", FtpsDesiredState.On);

        Assert.Equal(expected, status.Certificate);
    }

    /// <summary>A certificate outcome the module has no name for is refused, not rendered as another one.</summary>
    [Fact]
    public void A_certificate_outcome_the_module_cannot_name_is_refused()
    {
        var unknown = (AgentFtpsCertificate)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => FtpsStatusMapper.ToDto(
            Observation(running: true, answered: true, unknown), "ftp.example.test", FtpsDesiredState.On));
    }

    /// <summary>Each TLS enforcement outcome the agent client reports reaches the screen as itself.</summary>
    /// <remarks>
    /// Three outcomes, one row each, so a swap of two of them (the quiet one is "not observed" read as
    /// "not enforced") fails by name. Not observed is the row an unreadable configuration produces.
    /// </remarks>
    /// <param name="observed">What the agent client reports.</param>
    /// <param name="expected">What the screen is told.</param>
    [Theory]
    [InlineData(AgentFtpsTlsEnforcement.Enforced, FtpsTlsEnforcement.Enforced)]
    [InlineData(AgentFtpsTlsEnforcement.NotEnforced, FtpsTlsEnforcement.NotEnforced)]
    [InlineData(AgentFtpsTlsEnforcement.NotObserved, FtpsTlsEnforcement.NotObserved)]
    public void Each_tls_enforcement_outcome_reaches_the_screen_as_itself(
        AgentFtpsTlsEnforcement observed,
        FtpsTlsEnforcement expected)
    {
        var dto = FtpsStatusMapper.ToDto(
            Observation(running: true, answered: true, tlsEnforcement: observed),
            "ftp.example.test",
            FtpsDesiredState.On);

        Assert.Equal(expected, dto.TlsEnforcement);
    }

    /// <summary>A TLS enforcement outcome the module cannot name is refused.</summary>
    [Fact]
    public void A_tls_enforcement_outcome_the_module_cannot_name_is_refused()
    {
        var unknown = (AgentFtpsTlsEnforcement)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => FtpsStatusMapper.ToDto(
            Observation(running: true, answered: true, tlsEnforcement: unknown),
            "ftp.example.test",
            FtpsDesiredState.On));
    }

    /// <summary>Builds an agent observation with the two facts the agreement reads.</summary>
    /// <param name="running">Whether the unit is active.</param>
    /// <param name="answered">Whether the control port greeted.</param>
    /// <param name="certificate">What the agent found at the certificate path; installed material by default.</param>
    /// <param name="tlsEnforcement">Whether the live configuration forces TLS; enforced by default.</param>
    /// <returns>An otherwise healthy observation.</returns>
    private static AgentFtpsStatus Observation(
        bool running,
        bool answered,
        AgentFtpsCertificate certificate = AgentFtpsCertificate.Provided,
        AgentFtpsTlsEnforcement tlsEnforcement = AgentFtpsTlsEnforcement.Enforced)
    {
        return new AgentFtpsStatus(
            Running: running,
            ControlPortAnswered: answered,
            Certificate: certificate,
            CertificatePath: "/etc/maran/certificates/ftp.example.test",
            PassivePortMin: 30000,
            PassivePortMax: 30099,
            Ipv4Only: false,
            TlsEnforcement: tlsEnforcement);
    }
}
