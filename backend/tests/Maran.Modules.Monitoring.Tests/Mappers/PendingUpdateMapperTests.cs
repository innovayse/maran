using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Mappers;

namespace Maran.Modules.Monitoring.Tests.Mappers;

/// <summary>Behavioral contract of <see cref="PendingUpdateMapper"/>.</summary>
public sealed class PendingUpdateMapperTests
{
    /// <summary>Every field crosses as it arrived, including an absent installed version and source.</summary>
    /// <param name="installed">The installed version the agent reported, or <c>null</c>.</param>
    /// <param name="source">The source the agent reported, possibly empty.</param>
    [Theory]
    [InlineData("1.0", "updates")]
    [InlineData(null, "")]
    public void Every_field_crosses_as_it_arrived(string? installed, string source)
    {
        var dto = PendingUpdateMapper.ToDto(new AgentPendingUpdate("bash", "2.0", installed, source));

        Assert.Equal(new PendingUpdateDto("bash", "2.0", installed, source), dto);
    }
}
