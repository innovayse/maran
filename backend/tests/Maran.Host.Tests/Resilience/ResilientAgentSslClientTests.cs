using Maran.Agent.Client.Services.SitesService;
using Maran.Host.Resilience;
using Maran.Host.Tests.Fixtures;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.Resilience;

/// <summary>What the TLS decorator does: the call goes through the pipeline, arguments unchanged.</summary>
public sealed class ResilientAgentSslClientTests
{
    /// <summary>Deadline for any test that waits on the pipeline.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A transport outage on an install is answered as an unavailable agent, not thrown.</summary>
    /// <remarks>
    /// The inner fake throws what the real transport throws for a refused connection
    /// (<see cref="AgentTransportFaults.ConnectRefused"/>), so the answer is the decorator's to give: the
    /// pipeline retries the refused connect twice and the shared mapping answers 503's error code.
    /// </remarks>
    [Fact]
    public async Task A_transport_outage_on_an_install_answers_agent_unavailable_instead_of_throwing()
    {
        var inner = new RecordingAgentSslClient { FailuresBeforeSuccess = int.MaxValue };

        var result = await InstallAsync(inner).WaitAsync(TestTimeout);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
        Assert.Equal(3, inner.Calls);
    }

    /// <summary>A site descriptor with every field set, to check it arrives unchanged.</summary>
    private static readonly SiteDescriptor Descriptor =
        new(["www.example.com"], SiteBackendKind.Php, "8.3", "127.0.0.1:3000", true);

    /// <summary>Install retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task Install_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentSslClient { FailuresBeforeSuccess = 1 };

        var result = await InstallAsync(inner).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>Every argument reaches the inner client unchanged.</summary>
    [Fact]
    public async Task Every_argument_reaches_the_inner_client_unchanged()
    {
        var inner = new RecordingAgentSslClient();

        await InstallAsync(inner).WaitAsync(TestTimeout);

        Assert.Equal("acc1", inner.LastAccountUsername);
        Assert.Equal("example.com", inner.LastDomain);
        Assert.Equal("-----BEGIN CERTIFICATE-----", inner.LastCertificatePem);
        Assert.Equal("-----BEGIN PRIVATE KEY-----", inner.LastPrivateKeyPem);
        Assert.Same(Descriptor, inner.LastSite);
    }

    /// <summary>Calls the decorated install path with fixed arguments.</summary>
    /// <param name="inner">The recording client to wrap.</param>
    /// <returns>What the decorated call returned.</returns>
    private static async Task<SharedKernel.Results.Result<Agent.Client.Services.SslService.InstalledCertificateDto>>
        InstallAsync(RecordingAgentSslClient inner)
    {
        var client = new ResilientAgentSslClient(
            inner,
            OperationPipelineRegistry.WithOperationTimeout(30));

        return await client.InstallCertificateAsync(
            "acc1",
            "example.com",
            "-----BEGIN CERTIFICATE-----",
            "-----BEGIN PRIVATE KEY-----",
            Descriptor,
            default);
    }
}
