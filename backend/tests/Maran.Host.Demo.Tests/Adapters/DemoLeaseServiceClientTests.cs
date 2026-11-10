using Maran.Host.Demo.Adapters;
using Maran.Modules.Licensing.Domain.Enums;

namespace Maran.Host.Demo.Tests.Adapters;

/// <summary>The demonstration's licence service client contacts nobody and says there is no service.</summary>
public sealed class DemoLeaseServiceClientTests
{
    /// <summary>Whatever licence id is asked about, the answer is "no licence service" with no body.</summary>
    [Theory]
    [InlineData("lic-001")]
    [InlineData("demo")]
    public async Task Every_request_is_answered_as_not_configured_without_a_body(string licenceId)
    {
        var answer = await new DemoLeaseServiceClient().RequestAsync(licenceId, CancellationToken.None);

        Assert.Equal(LeaseServiceAnswerKind.NotConfigured, answer.Kind);
        Assert.Null(answer.Body);
    }
}
