using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Grpc.Core;
using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Models;
using Maran.Agent.Client.Resources;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Channels;

/// <summary>
/// The mutual-TLS channel against a real TLS listener that requires a client certificate. The double
/// answers 404 to everything, which the gRPC client reports as <c>Unimplemented</c>: reaching it proves
/// the handshake, the pin and HTTP/2 all worked, and being refused before it is an unreachable node (<c>AgentTransportFailure.IsUnreachable</c>).
/// </summary>
public sealed class AgentChannelMutualTlsTests : IDisposable
{
    /// <summary>Deadline for any call to a double, so a stalled transport fails instead of hanging.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The panel's own identity, presented to every node.</summary>
    private readonly X509Certificate2 _panelIdentity = TestCertificates.CreateValid("panel");

    /// <summary>Releases the panel identity.</summary>
    public void Dispose()
    {
        _panelIdentity.Dispose();
    }

    /// <summary>
    /// A node presenting exactly the pinned certificate is reached, and it receives the panel's client
    /// certificate: the control that makes every refusal below mean something.
    /// </summary>
    [Fact]
    public async Task A_node_presenting_the_pinned_certificate_is_reached_and_sees_the_panel_identity()
    {
        using var nodeCertificate = TestCertificates.CreateValid("node");
        await using var node = await MutualTlsNodeDouble.StartAsync(nodeCertificate, SslProtocols.Tls13);

        var failure = await CallAsync(node, TestCertificates.Fingerprint(nodeCertificate));

        Assert.Equal(StatusCode.Unimplemented, failure.StatusCode);
        Assert.False(AgentTransportFailure.IsUnreachable(failure));
        Assert.Equal(_panelIdentity.Thumbprint, node.ClientCertificateSeen?.Thumbprint);
    }

    /// <summary>A node whose certificate is not the approved one is refused before any request is sent.</summary>
    [Fact]
    public async Task A_node_presenting_another_certificate_is_refused()
    {
        using var nodeCertificate = TestCertificates.CreateValid("node");
        using var approved = TestCertificates.CreateValid("node");
        await using var node = await MutualTlsNodeDouble.StartAsync(nodeCertificate, SslProtocols.Tls13);

        var failure = await CallAsync(node, TestCertificates.Fingerprint(approved));

        AssertUnreachable(failure);
        Assert.Null(node.ClientCertificateSeen);
    }

    /// <summary>The pinned certificate past its end date is refused, so expiry stops the node rather than passing silently.</summary>
    [Fact]
    public async Task A_node_whose_pinned_certificate_has_expired_is_refused()
    {
        using var nodeCertificate = TestCertificates.Create(
            "node",
            TestCertificates.Now.AddDays(-400),
            TestCertificates.Now.AddDays(-1));
        await using var node = await MutualTlsNodeDouble.StartAsync(nodeCertificate, SslProtocols.Tls13);

        var failure = await CallAsync(node, TestCertificates.Fingerprint(nodeCertificate));

        AssertUnreachable(failure);
    }

    /// <summary>A node that offers nothing newer than TLS 1.2 is refused rather than downgraded to.</summary>
    [Fact]
    public async Task A_node_offering_only_tls_1_2_is_refused()
    {
        using var nodeCertificate = TestCertificates.CreateValid("node");
        await using var node = await MutualTlsNodeDouble.StartAsync(nodeCertificate, SslProtocols.Tls12);

        var failure = await CallAsync(node, TestCertificates.Fingerprint(nodeCertificate));

        AssertUnreachable(failure);
    }

    /// <summary>Makes one call through a channel pinned to <paramref name="pin"/> and returns the failure it ended with.</summary>
    /// <param name="node">The node to call.</param>
    /// <param name="pin">The fingerprint the channel accepts.</param>
    /// <returns>The failure the call ended with.</returns>
    private async Task<RpcException> CallAsync(MutualTlsNodeDouble node, string pin)
    {
        var endpoint = new MutualTlsAgentEndpoint("127.0.0.1", node.Port, _panelIdentity, pin);
        using var channel = AgentChannel.CreateMutualTls(endpoint, new FixedClock(TestCertificates.Now));
        var client = new Maran.Agent.V1.SystemService.SystemServiceClient(channel);

        var failure = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.GetAgentInfoAsync(new GetAgentInfoRequest()).ResponseAsync.WaitAsync(TestTimeout);
        });
        return failure;
    }

    /// <summary>Asserts the panel classifies <paramref name="failure"/> as an unreachable node, answered <c>AgentUnavailable</c>.</summary>
    /// <param name="failure">The failure a refused call ended with.</param>
    private static void AssertUnreachable(RpcException failure)
    {
        Assert.True(AgentTransportFailure.IsUnreachable(failure));
        Assert.Equal(nameof(ErrorMessages.AgentUnavailable), AgentTransportFailure.ToError(failure).Code);
    }
}
