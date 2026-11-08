using System.Resources;
using Maran.Agent.Client.Extensions;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AccountsService;
using Maran.Agent.Client.Services.BackupService;
using Maran.Agent.Client.Services.CronService;
using Maran.Agent.Client.Services.DbService;
using Maran.Agent.Client.Services.DnsService;
using Maran.Agent.Client.Services.FilesService;
using Maran.Agent.Client.Services.FirewallService;
using Maran.Agent.Client.Services.FtpsService;
using Maran.Agent.Client.Services.MailService;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SftpService;
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
    /// Registers the typed agent clients over the routed channel pool (the local unix socket unless a
    /// registry of servers supplies others).
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="socketPath">
    /// Path to the agent's unix socket (configuration key <c>Agent:SocketPath</c>;
    /// production default <c>/run/maran/agent.sock</c>).
    /// </param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddAgentClient(this IServiceCollection services, string socketPath)
    {
        // Routing owns the channels: the pool is a singleton the container disposes on shutdown, and
        // every per-service client asks IAgentChannels for the channel of the server the current
        // call targets instead of holding one connection of its own.
        services.AddAgentRouting(socketPath);
        services.AddSingleton<IAgentAccountsClient>(
            provider =>
            {
                return new AgentAccountsClient(
                    provider.GetRequiredService<IAgentChannels>(),
                    provider.GetRequiredService<ILogger<AgentAccountsClient>>());
            });
        services.AddSingleton<IAgentSitesClient>(
            provider =>
            {
                return new AgentSitesClient(
                    provider.GetRequiredService<IAgentChannels>(),
                    provider.GetRequiredService<ILogger<AgentSitesClient>>());
            });
        services.AddSingleton<IAgentSslClient>(
            provider =>
            {
                return new AgentSslClient(
                    provider.GetRequiredService<IAgentChannels>(),
                    provider.GetRequiredService<ILogger<AgentSslClient>>());
            });
        services.AddSingleton<IAgentPhpClient>(
            provider =>
            {
                return new AgentPhpClient(
                    provider.GetRequiredService<IAgentChannels>(),
                    provider.GetRequiredService<ILogger<AgentPhpClient>>());
            });
        services.AddSingleton<IAgentFilesClient>(
            provider =>
            {
                return new AgentFilesClient(
                    provider.GetRequiredService<IAgentChannels>(),
                    provider.GetRequiredService<ILogger<AgentFilesClient>>());
            });
        services.AddSingleton<IAgentDbClient>(
            provider =>
            {
                return new AgentDbClient(
                    provider.GetRequiredService<IAgentChannels>(),
                    provider.GetRequiredService<ILogger<AgentDbClient>>());
            });
        services.AddSingleton<IAgentSftpClient>(
            provider =>
            {
                return new AgentSftpClient(
                    provider.GetRequiredService<IAgentChannels>(),
                    provider.GetRequiredService<ILogger<AgentSftpClient>>());
            });
        services.AddSingleton<IAgentFtpsClient>(
            provider =>
            {
                return new AgentFtpsClient(
                    provider.GetRequiredService<IAgentChannels>(),
                    provider.GetRequiredService<ILogger<AgentFtpsClient>>());
            });
        services.AddSingleton<IAgentMailClient>(
            provider =>
            {
                return new AgentMailClient(
                    provider.GetRequiredService<IAgentChannels>(),
                    provider.GetRequiredService<ILogger<AgentMailClient>>());
            });
        services.AddSingleton<IAgentCronClient>(
            provider =>
            {
                return new AgentCronClient(
                    provider.GetRequiredService<IAgentChannels>(),
                    provider.GetRequiredService<ILogger<AgentCronClient>>());
            });
        services.AddSingleton<IAgentFirewallClient>(
            provider =>
            {
                return new AgentFirewallClient(
                    provider.GetRequiredService<IAgentChannels>(),
                    provider.GetRequiredService<ILogger<AgentFirewallClient>>());
            });
        services.AddSingleton<IAgentMonitorClient>(
            provider =>
            {
                return new AgentMonitorClient(
                    provider.GetRequiredService<IAgentChannels>(),
                    provider.GetRequiredService<ILogger<AgentMonitorClient>>());
            });
        services.AddSingleton<IAgentBackupClient>(
            provider =>
            {
                return new AgentBackupClient(
                    provider.GetRequiredService<IAgentChannels>(),
                    provider.GetRequiredService<ILogger<AgentBackupClient>>());
            });
        services.AddSingleton<IAgentDnsClient>(
            provider =>
            {
                return new AgentDnsClient(
                    provider.GetRequiredService<IAgentChannels>(),
                    provider.GetRequiredService<ILogger<AgentDnsClient>>());
            });
        services.AddSingleton<IAgentSystemClient>(
            provider =>
            {
                return new AgentSystemClient(
                    provider.GetRequiredService<IAgentChannels>(),
                    provider.GetRequiredService<ILogger<AgentSystemClient>>());
            });

        // Registers this project's resource manager into the shared pool the panel-wide
        // ResxErrorTextProvider resolves error codes against (rules/csharp.md "The backend owns all
        // user-facing message text"). Without it every Agent* code this project produces is claimed
        // by no resource file, and the provider's last-resort fallback shows the customer the
        // machine code itself instead of a sentence. The codes are the <data> keys of
        // Resources/ErrorMessages.resx (grep -c '<data name="Agent' Resources/ErrorMessages.resx
        // counts them); no number is written here, because the one that was here had gone stale.
        services.AddSingleton(
            new ResourceManager(ErrorMessagesResourceBaseName, typeof(DependencyInjection).Assembly));

        return services;
    }
}
