using System.Text.Json;
using Maran.Host.Dashboard;

namespace Maran.Host.Tests.Dashboard;

/// <summary>Behavioral contract of the dashboard's audience union: the shape says who it was filled for.</summary>
public sealed class DashboardAudienceTests
{
    /// <summary>A customer's dashboard serializes as its audience and nothing else, when written as the declared union.</summary>
    [Fact]
    public void A_customer_dashboard_serializes_as_its_audience_and_nothing_else()
    {
        var json = JsonSerializer.Serialize<DashboardDto>(
            new CustomerDashboardDto(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("{\"audience\":\"customer\"}", json);
    }
}
