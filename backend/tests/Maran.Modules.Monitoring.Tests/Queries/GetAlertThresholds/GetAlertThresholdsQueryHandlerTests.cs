using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Queries.GetAlertThresholds;
using Maran.Modules.Monitoring.Tests.TestSupport;

namespace Maran.Modules.Monitoring.Tests.Queries.GetAlertThresholds;

/// <summary>Reading the alert thresholds: the defaults on an unconfigured panel, the stored figures after a save.</summary>
public sealed class GetAlertThresholdsQueryHandlerTests
{
    /// <summary>A panel nobody configured reports the defaults and the bounds.</summary>
    [Fact]
    public async Task A_panel_nobody_configured_reports_the_defaults_and_the_bounds()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var handler = new GetAlertThresholdsQueryHandler(dbContext, new FakeClock());

        var result = await handler.HandleAsync(new GetAlertThresholdsQuery(), CancellationToken.None);

        var dto = result.Value!;
        Assert.Equal(90, dto.DiskPercent);
        Assert.Null(dto.CpuPercent);
        Assert.Null(dto.MemoryPercent);
        Assert.Equal(50, dto.MinimumPercent);
        Assert.Equal(99, dto.MaximumPercent);
        Assert.Empty(dbContext.AlertThresholds);
    }

    /// <summary>Stored figures are reported, an unwatched condition as null.</summary>
    [Fact]
    public async Task Stored_figures_are_reported_an_unwatched_condition_as_null()
    {
        await using var dbContext = MonitoringTestContext.Create();
        var stored = AlertThresholds.CreateDefault(new FakeClock().UtcNow);
        stored.Replace(80, null, 70, new FakeClock().UtcNow);
        dbContext.AlertThresholds.Add(stored);
        await dbContext.SaveChangesAsync();
        var handler = new GetAlertThresholdsQueryHandler(dbContext, new FakeClock());

        var dto = (await handler.HandleAsync(new GetAlertThresholdsQuery(), CancellationToken.None)).Value!;

        Assert.Equal(80, dto.DiskPercent);
        Assert.Null(dto.CpuPercent);
        Assert.Equal(70, dto.MemoryPercent);
    }
}
