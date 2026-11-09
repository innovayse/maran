using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Mappers;

namespace Maran.Modules.Mail.Tests.Mappers;

/// <summary>Behaviour of the mapper that restates the agent's observation as the status screen's shape.</summary>
public sealed class MailServerStatusMapperTests
{
    /// <summary>Every agent daemon maps to its own kind, the whole vocabulary.</summary>
    /// <param name="daemon">The agent's value.</param>
    /// <param name="expected">This module's value.</param>
    [Theory]
    [InlineData(AgentMailDaemon.Mta, MailDaemonKind.Mta)]
    [InlineData(AgentMailDaemon.ImapServer, MailDaemonKind.ImapServer)]
    [InlineData(AgentMailDaemon.SpamFilter, MailDaemonKind.SpamFilter)]
    [InlineData(AgentMailDaemon.RateLimitStore, MailDaemonKind.RateLimitStore)]
    [InlineData(AgentMailDaemon.VirusScanner, MailDaemonKind.VirusScanner)]
    public void Every_agent_daemon_maps_to_its_own_kind(AgentMailDaemon daemon, MailDaemonKind expected)
    {
        var observed = Observation() with { Daemons = [Daemon(daemon)] };

        var dto = MailServerStatusMapper.ToDto(observed, null);

        Assert.Equal(expected, Assert.Single(dto.Daemons).Kind);
    }

    /// <summary>The three observations keep "could not tell" apart from "no".</summary>
    /// <param name="observed">The agent's value.</param>
    /// <param name="expected">This module's value.</param>
    [Theory]
    [InlineData(AgentMailObservation.NotObserved, MailObservation.NotObserved)]
    [InlineData(AgentMailObservation.Yes, MailObservation.Yes)]
    [InlineData(AgentMailObservation.No, MailObservation.No)]
    public void The_three_observations_stay_distinct(AgentMailObservation observed, MailObservation expected)
    {
        var dto = MailServerStatusMapper.ToDto(
            Observation() with { TlsInUse = observed, SieveAvailable = observed, Enabled = observed },
            null);

        Assert.Equal(expected, dto.TlsInUse);
        Assert.Equal(expected, dto.SieveAvailable);
        Assert.Equal(expected, dto.ObservedEnabled);
    }

    /// <summary>The password scheme maps, and an unread one stays null.</summary>
    [Fact]
    public void The_password_scheme_maps_and_an_unread_one_stays_null()
    {
        var blowfish = MailServerStatusMapper.ToDto(
            Observation() with { PasswordScheme = AgentMailPasswordScheme.BlowfishCrypt }, null);
        var argon = MailServerStatusMapper.ToDto(
            Observation() with { PasswordScheme = AgentMailPasswordScheme.Argon2id }, null);
        var unread = MailServerStatusMapper.ToDto(Observation() with { PasswordScheme = null }, null);

        Assert.Equal(MailPasswordScheme.BlowfishCrypt, blowfish.PasswordScheme);
        Assert.Equal(MailPasswordScheme.Argon2id, argon.PasswordScheme);
        Assert.Null(unread.PasswordScheme);
    }

    /// <summary>The ports to open are the module's expected ports, whatever the host reports.</summary>
    [Fact]
    public void The_ports_to_open_are_the_modules_expected_ports()
    {
        var dto = MailServerStatusMapper.ToDto(Observation(), null);

        Assert.Equal(MailDefaults.ExpectedPorts, dto.Ports);
    }

    /// <summary>The queue size is carried, and an unread one stays null instead of becoming zero.</summary>
    [Fact]
    public void An_unread_queue_size_stays_null()
    {
        var read = MailServerStatusMapper.ToDto(Observation() with { QueuedMessages = 7 }, null);
        var unread = MailServerStatusMapper.ToDto(Observation() with { QueuedMessages = null }, null);

        Assert.Equal(7u, read.QueuedMessages);
        Assert.Null(unread.QueuedMessages);
    }

    /// <summary>Builds an observation with every field set.</summary>
    /// <returns>The observation.</returns>
    private static AgentMailStatus Observation()
    {
        return new AgentMailStatus(
            [],
            AgentMailObservation.Yes,
            "mail.example.com",
            AgentMailObservation.Yes,
            2,
            4,
            AgentMailPasswordScheme.Argon2id,
            AgentMailObservation.Yes,
            0,
            AgentMailObservation.Yes);
    }

    /// <summary>Builds one daemon report.</summary>
    /// <param name="daemon">Which daemon.</param>
    /// <returns>The report.</returns>
    private static AgentMailDaemonStatus Daemon(AgentMailDaemon daemon)
    {
        return new AgentMailDaemonStatus(
            daemon,
            AgentMailObservation.Yes,
            "1.0",
            AgentMailObservation.Yes,
            [],
            AgentMailObservation.Yes);
    }
}
