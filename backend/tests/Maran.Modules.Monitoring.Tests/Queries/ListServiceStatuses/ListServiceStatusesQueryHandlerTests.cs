using System.Globalization;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Queries.ListServiceStatuses;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Monitoring.Tests.Queries.ListServiceStatuses;

/// <summary>
/// Covers the projection the services card is drawn from: each agent row crosses with its
/// machine-stable member, its localized name, its state and its detail — nothing collapsed,
/// nothing invented.
/// </summary>
public sealed class ListServiceStatusesQueryHandlerTests
{
    /// <summary>A row crosses with the localized name beside the machine member, not instead of it.</summary>
    /// <remarks>
    /// Both halves matter and are asserted in one test because they are one projection: the name is
    /// what an operator reads (the pinned defect was the card printing <c>webServer</c> in a Russian
    /// interface), and the member is what a client keys the row by — a fix that replaced one with
    /// the other would trade the defect for a different one.
    /// </remarks>
    [Fact]
    public async Task A_row_crosses_with_its_localized_name_beside_the_machine_member()
    {
        var agent = new StubAgentMonitorClient
        {
            Statuses = Result<IReadOnlyList<AgentServiceStatus>>.Ok(
            [
                new AgentServiceStatus(AgentManagedService.WebServer, AgentServiceState.Running, "active (running)"),
            ]),
        };
        var handler = new ListServiceStatusesQueryHandler(agent, MonitoringTestContext.ServiceNames());
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            // The request culture the pinned defect was measured in. Through the REAL resource
            // files, so this asserts the Russian VALUE an operator reads, not a stubbed echo.
            CultureInfo.CurrentUICulture = new CultureInfo("ru");

            var result = await handler.HandleAsync(new ListServiceStatusesQuery(), CancellationToken.None);

            Assert.True(result.IsSuccess);
            var row = Assert.Single(result.Value!);
            Assert.Equal(AgentManagedService.WebServer, row.Service);
            Assert.Equal("Веб-сервер", row.Name);
            Assert.Equal(AgentServiceState.Running, row.State);
            Assert.Equal("active (running)", row.Detail);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>The agent's refusal crosses as the same typed failure, with nothing projected.</summary>
    [Fact]
    public async Task The_agents_refusal_crosses_as_the_same_typed_failure()
    {
        var agent = new StubAgentMonitorClient
        {
            Statuses = Result<IReadOnlyList<AgentServiceStatus>>.Fail(
                Error.Of("AgentSystemFailure", ErrorType.Failure)),
        };
        var handler = new ListServiceStatusesQueryHandler(agent, MonitoringTestContext.ServiceNames());

        var result = await handler.HandleAsync(new ListServiceStatusesQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }
}
