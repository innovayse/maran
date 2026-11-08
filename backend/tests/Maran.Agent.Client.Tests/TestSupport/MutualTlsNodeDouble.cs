using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Tests.TestSupport;

/// <summary>
/// A managed node's fleet listener, reduced to what the panel's channel can observe: a loopback HTTP/2
/// endpoint behind TLS that REQUIRES a client certificate and answers every request with 404, which
/// the gRPC client reports as <c>Unimplemented</c>. A call that gets that far has completed the TLS
/// handshake, negotiated HTTP/2 and been served, so "reached the node" and "was refused before the
/// node" are told apart by status alone: <c>Unimplemented</c> against <c>Unavailable</c>.
/// </summary>
internal sealed class MutualTlsNodeDouble : IAsyncDisposable
{
    /// <summary>The running host.</summary>
    private readonly WebApplication _app;

    /// <summary>The certificate a request arrived with, or <c>null</c> when none has arrived.</summary>
    private volatile X509Certificate2? _clientCertificateSeen;

    /// <summary>The loopback port the double listens on.</summary>
    public int Port { get; }

    /// <summary>The client certificate of the latest request that reached the node, if any.</summary>
    public X509Certificate2? ClientCertificateSeen
    {
        get { return _clientCertificateSeen; }
    }

    /// <summary>Creates the double around a started host.</summary>
    /// <param name="app">The started host.</param>
    /// <param name="port">The port it bound.</param>
    private MutualTlsNodeDouble(WebApplication app, int port)
    {
        _app = app;
        Port = port;
    }

    /// <summary>Starts a node that presents <paramref name="serverCertificate"/> and requires a client certificate.</summary>
    /// <param name="serverCertificate">The node's identity, with its private key.</param>
    /// <param name="protocols">The TLS versions the node accepts.</param>
    /// <returns>The running node.</returns>
    public static async Task<MutualTlsNodeDouble> StartAsync(
        X509Certificate2 serverCertificate,
        SslProtocols protocols)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, 0, listen =>
            {
                listen.Protocols = HttpProtocols.Http2;
                listen.UseHttps(https =>
                {
                    https.ServerCertificate = serverCertificate;
                    https.SslProtocols = protocols;
                    https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    https.ClientCertificateValidation = (_, _, _) =>
                    {
                        return true;
                    };
                });
            });
        });

        var app = builder.Build();
        MutualTlsNodeDouble? node = null;
        app.Run(context =>
        {
            node!._clientCertificateSeen = context.Connection.ClientCertificate;
            context.Response.StatusCode = 404;
            return Task.CompletedTask;
        });
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        node = new MutualTlsNodeDouble(app, new Uri(address).Port);
        return node;
    }

    /// <summary>Stops the node.</summary>
    /// <returns>A task that completes when the host has stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
    }
}
