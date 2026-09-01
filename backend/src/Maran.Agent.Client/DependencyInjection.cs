using System.Resources;
using Grpc.Net.Client;
using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AccountsService;
using Maran.Agent.Client.Services.FilesService;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Client.Services.SslService;
using Maran.Agent.Client.Services.SystemService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client;

/// <summary>
/// Registration entry point of the agent client. Every project exposes exactly
/// one <c>Add&lt;Project&gt;</c> method here, so the Host never news up agent
/// types itself and <c>Program.cs</c> stays a table of contents.
/// </summary>
public static class DependencyInjection
{
    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Agent.Client.Resources.ErrorMessages";

    /// <summary>
    /// Registers the typed agent clients over one shared unix-socket channel.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="socketPath">
    /// Path to the agent's unix socket (configuration key <c>Agent:SocketPath</c>;
    /// production default <c>/run/maran/agent.sock</c>).
    /// </param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddAgentClient(this IServiceCollection services, string socketPath)
    {
        // The channel is its own singleton so the container owns its lifetime and disposes it on
        // shutdown; every future per-service client resolves this one channel rather than opening
        // a second connection to the same socket.
        services.AddSingleton(_ =>
        {
            return AgentChannel.CreateUnixSocket(socketPath);
        });
        services.AddSingleton<IAgentAccountsClient>(
            provider =>
            {
                return new AgentAccountsClient(
                    provider.GetRequiredService<GrpcChannel>(),
                    provider.GetRequiredService<ILogger<AgentAccountsClient>>());
            });
        services.AddSingleton<IAgentSitesClient>(
            provider =>
            {
                return new AgentSitesClient(
                    provider.GetRequiredService<GrpcChannel>(),
                    provider.GetRequiredService<ILogger<AgentSitesClient>>());
            });
        services.AddSingleton<IAgentSslClient>(
            provider =>
            {
                return new AgentSslClient(
                    provider.GetRequiredService<GrpcChannel>(),
                    provider.GetRequiredService<ILogger<AgentSslClient>>());
            });
        services.AddSingleton<IAgentPhpClient>(
            provider =>
            {
                return new AgentPhpClient(
                    provider.GetRequiredService<GrpcChannel>(),
                    provider.GetRequiredService<ILogger<AgentPhpClient>>());
            });
        services.AddSingleton<IAgentFilesClient>(
            provider =>
            {
                return new AgentFilesClient(
                    provider.GetRequiredService<GrpcChannel>(),
                    provider.GetRequiredService<ILogger<AgentFilesClient>>());
            });
        services.AddSingleton<IAgentSystemClient>(
            provider =>
            {
                return new AgentSystemClient(
                    provider.GetRequiredService<GrpcChannel>(),
                    provider.GetRequiredService<ILogger<AgentSystemClient>>());
            });

        // Registers this project's resource manager into the shared pool the panel-wide
        // ResxErrorTextProvider resolves error codes against (rules/csharp.md "The backend owns all
        // user-facing message text"). Without it the seven Agent* codes this project produces are
        // claimed by no resource file, and the provider's last-resort fallback shows the customer
        // the machine code itself instead of a sentence.
        services.AddSingleton(
            new ResourceManager(ErrorMessagesResourceBaseName, typeof(DependencyInjection).Assembly));

        return services;
    }
}
