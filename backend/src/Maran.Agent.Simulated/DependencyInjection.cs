using Maran.Agent.Client.Interfaces;
using Maran.Agent.Simulated.Interfaces;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Seeds;
using Maran.Agent.Simulated.Services.AccountsService;
using Maran.Agent.Simulated.Services.BackupService;
using Maran.Agent.Simulated.Services.CronService;
using Maran.Agent.Simulated.Services.DbService;
using Maran.Agent.Simulated.Services.DnsService;
using Maran.Agent.Simulated.Services.FilesService;
using Maran.Agent.Simulated.Services.FirewallService;
using Maran.Agent.Simulated.Services.FtpsService;
using Maran.Agent.Simulated.Services.MailService;
using Maran.Agent.Simulated.Services.MonitorService;
using Maran.Agent.Simulated.Services.PhpService;
using Maran.Agent.Simulated.Services.SftpService;
using Maran.Agent.Simulated.Services.SitesService;
using Maran.Agent.Simulated.Services.SslService;
using Maran.Agent.Simulated.Services.SystemService;
using Maran.Agent.Simulated.State;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maran.Agent.Simulated;

/// <summary>This project's registrations.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers a simulated agent in place of the real one: the ledger that holds its state and one
    /// in-memory implementation of every <c>IAgent*Client</c> contract.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <returns>The same container, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// A host calls this INSTEAD of <c>AddAgentClient</c>, never beside it: both would register the
    /// same contracts, and the one registered last would win without anyone having chosen it. The
    /// clients are singletons like the real ones, and the ledger is a singleton too, so the whole
    /// process sees one host.
    /// </para>
    /// <para>
    /// The ledger is built on first use and every registered <see cref="ISimulatedAgentSeed"/> is
    /// applied to it in registration order, the baseline seed first. A host that wants a populated
    /// server registers its own seed after this call; one that wants smaller caps registers its own
    /// <see cref="SimulatedAgentLimits"/> before it, which this call then leaves alone.
    /// </para>
    /// <para>
    /// The clock the clients read is the container's <see cref="IClock"/>, registered by
    /// <c>AddSharedKernel</c>; this project registers none of its own.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSimulatedAgent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(SimulatedAgentLimits.Default);
        services.AddSingleton<ISimulatedAgentSeed, SimulatedBaselineSeed>();
        services.AddSingleton(
            provider =>
            {
                var ledger = new SimulatedAgentLedger(provider.GetRequiredService<SimulatedAgentLimits>());
                foreach (var seed in provider.GetServices<ISimulatedAgentSeed>())
                {
                    seed.ApplyTo(ledger);
                }

                return ledger;
            });

        services.AddSingleton<IAgentSystemClient, SimulatedAgentSystemClient>();
        services.AddSingleton<IAgentAccountsClient, SimulatedAgentAccountsClient>();
        services.AddSingleton<IAgentSitesClient, SimulatedAgentSitesClient>();
        services.AddSingleton<IAgentPhpClient, SimulatedAgentPhpClient>();
        services.AddSingleton<IAgentSslClient, SimulatedAgentSslClient>();
        services.AddSingleton<IAgentMonitorClient, SimulatedAgentMonitorClient>();
        services.AddSingleton<IAgentDbClient, SimulatedAgentDbClient>();
        services.AddSingleton<IAgentSftpClient, SimulatedAgentSftpClient>();
        services.AddSingleton<IAgentFtpsClient, SimulatedAgentFtpsClient>();
        services.AddSingleton<IAgentDnsClient, SimulatedAgentDnsClient>();
        services.AddSingleton<IAgentMailClient, SimulatedAgentMailClient>();
        services.AddSingleton<IAgentFilesClient, SimulatedAgentFilesClient>();
        services.AddSingleton<IAgentCronClient, SimulatedAgentCronClient>();
        services.AddSingleton<IAgentFirewallClient, SimulatedAgentFirewallClient>();
        services.AddSingleton<IAgentBackupClient, SimulatedAgentBackupClient>();

        return services;
    }
}
