using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Queries.GetMailServerStatus;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Mail.Tests.Queries.GetMailServerStatus;

/// <summary>Behaviour of the query that reports what the mail server is configured and observed to do.</summary>
public sealed class GetMailServerStatusQueryHandlerTests
{
    /// <summary>The intention and the observation are reported side by side and never merged.</summary>
    [Fact]
    public async Task The_intention_and_the_observation_are_reported_side_by_side()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        context.Agent.Status = context.Agent.Status with { Enabled = AgentMailObservation.No };

        var result = await Handler(context).HandleAsync(new GetMailServerStatusQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Settings!.Enabled);
        Assert.Equal(MailObservation.No, result.Value.ObservedEnabled);
        Assert.Equal(["GetStatus()"], context.Agent.Calls);
    }

    /// <summary>A server nobody configured reports no settings, which is not "turned off".</summary>
    [Fact]
    public async Task A_server_nobody_configured_reports_no_settings()
    {
        using var context = new MailTestContext();

        var result = await Handler(context).HandleAsync(new GetMailServerStatusQuery(), CancellationToken.None);

        Assert.Null(result.Value.Settings);
    }

    /// <summary>A daemon the host reports is carried with what was and was not observed about it.</summary>
    [Fact]
    public async Task A_daemon_is_carried_with_what_was_and_was_not_observed_about_it()
    {
        using var context = new MailTestContext();
        context.Agent.Status = context.Agent.Status with
        {
            Daemons =
            [
                new AgentMailDaemonStatus(
                    AgentMailDaemon.Mta,
                    AgentMailObservation.Yes,
                    "3.8.1",
                    AgentMailObservation.Yes,
                    [new AgentMailListener("0.0.0.0", 25)],
                    AgentMailObservation.Yes),
                new AgentMailDaemonStatus(
                    AgentMailDaemon.VirusScanner,
                    AgentMailObservation.No,
                    string.Empty,
                    AgentMailObservation.NotObserved,
                    [],
                    AgentMailObservation.NotObserved),
            ],
        };

        var result = await Handler(context).HandleAsync(new GetMailServerStatusQuery(), CancellationToken.None);

        var mta = result.Value.Daemons[0];
        Assert.Equal(MailDaemonKind.Mta, mta.Kind);
        Assert.Equal("3.8.1", mta.Version);
        Assert.Equal(25u, Assert.Single(mta.Listeners).Port);
        var scanner = result.Value.Daemons[1];
        Assert.Equal(MailDaemonKind.VirusScanner, scanner.Kind);
        Assert.Equal(MailObservation.NotObserved, scanner.ListenersObserved);
        Assert.Empty(scanner.Listeners);
    }

    /// <summary>A failed agent read fails the query instead of presenting the intention as an observation.</summary>
    [Fact]
    public async Task A_failed_agent_read_fails_the_query()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        context.Agent.FailNext("GetStatus", Error.Of("AgentUnavailable", ErrorType.Failure));

        var result = await Handler(context).HandleAsync(new GetMailServerStatusQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
    }

    /// <summary>Builds the handler from the context's parts.</summary>
    /// <param name="context">The test context.</param>
    /// <returns>The handler.</returns>
    private static GetMailServerStatusQueryHandler Handler(MailTestContext context)
    {
        return new GetMailServerStatusQueryHandler(context.Database, context.Agent);
    }
}
