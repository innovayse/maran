using Maran.Agent.Client.Interfaces;
using Maran.Agent.Simulated.Interfaces;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.State;
using Maran.Agent.Simulated.Tests.TestSupport;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Agent.Simulated.Tests.Registration;

/// <summary>What the container hands out after a host swaps the real agent for the simulated one.</summary>
public sealed class AddSimulatedAgentTests
{
    /// <summary>The panel talks to the agent through sixteen contracts; the census must find all of them.</summary>
    private const int AgentContractCount = 17;

    /// <summary>
    /// Every <c>IAgent*Client</c> contract the agent client declares resolves to an implementation that
    /// lives in the simulated agent's assembly. The seventeen is the positive control: a census that
    /// found none, or a different number, would be reading the wrong assembly or a changed contract set.
    /// </summary>
    [Fact]
    public void Every_agent_contract_resolves_to_a_simulated_implementation()
    {
        var contracts = AgentContracts();
        using var provider = NewProvider(_ => { });

        Assert.Equal(AgentContractCount, contracts.Count);
        foreach (var contract in contracts)
        {
            var client = provider.GetRequiredService(contract);

            Assert.Same(typeof(SimulatedAgentLedger).Assembly, client.GetType().Assembly);
        }
    }

    /// <summary>A contract the census could not find would make the test above pass over nothing.</summary>
    [Fact]
    public void The_census_finds_the_contracts_by_the_names_the_agent_client_gives_them()
    {
        var names = AgentContracts().Select(contract => { return contract.Name; }).Order().ToList();

        Assert.Contains("IAgentSystemClient", names);
        Assert.Contains("IAgentFilesClient", names);
        Assert.DoesNotContain(names, name => { return name.EndsWith("ServiceInvoker", StringComparison.Ordinal); });
    }

    /// <summary>The clients are singletons like the real ones, so the whole process sees one host.</summary>
    [Fact]
    public void The_clients_and_the_ledger_are_singletons()
    {
        using var provider = NewProvider(_ => { });

        Assert.Same(
            provider.GetRequiredService<IAgentAccountsClient>(),
            provider.GetRequiredService<IAgentAccountsClient>());
        Assert.Same(
            provider.GetRequiredService<SimulatedAgentLedger>(),
            provider.GetRequiredService<SimulatedAgentLedger>());
    }

    /// <summary>The baseline seed runs even when the host adds no seed of its own.</summary>
    [Fact]
    public void The_ledger_starts_with_the_baseline_php_version()
    {
        using var provider = NewProvider(_ => { });

        Assert.True(provider.GetRequiredService<SimulatedAgentLedger>().PhpRuntimes.Contains("8.3"));
    }

    /// <summary>A seed the host registers is applied after the baseline, and its state is in the ledger.</summary>
    [Fact]
    public void A_host_seed_is_applied_after_the_baseline()
    {
        var seed = new MarkerSeed();
        using var provider = NewProvider(_ => { }, services => { services.AddSingleton<ISimulatedAgentSeed>(seed); });

        var ledger = provider.GetRequiredService<SimulatedAgentLedger>();

        Assert.True(seed.SawBaseline);
        Assert.True(ledger.Accounts.Contains(MarkerSeed.AccountName));
    }

    /// <summary>The same ledger the clients use is the one the seed filled.</summary>
    [Fact]
    public async Task The_clients_see_what_a_seed_wrote()
    {
        using var provider = NewProvider(
            _ => { },
            services => { services.AddSingleton<ISimulatedAgentSeed>(new MarkerSeed()); });

        var usage = await provider.GetRequiredService<IAgentAccountsClient>()
            .GetUsageAsync(MarkerSeed.AccountName, CancellationToken.None);

        Assert.True(usage.IsSuccess);
    }

    /// <summary>A host that registers its own caps first keeps them; the registration does not overwrite.</summary>
    [Fact]
    public void Limits_a_host_registered_first_are_kept()
    {
        using var provider = NewProvider(
            services => { services.AddSingleton(new SimulatedAgentLimits { MaxAccounts = 3 }); });

        Assert.Equal(3, provider.GetRequiredService<SimulatedAgentLedger>().Limits.MaxAccounts);
    }

    /// <summary>With no caps registered the defaults apply.</summary>
    [Fact]
    public void Without_registered_limits_the_defaults_apply()
    {
        using var provider = NewProvider(_ => { });

        Assert.Equal(
            SimulatedAgentLimits.Default.MaxAccounts,
            provider.GetRequiredService<SimulatedAgentLedger>().Limits.MaxAccounts);
    }

    /// <summary>Lists the contracts the census covers: the agent client's <c>IAgent*Client</c> interfaces.</summary>
    /// <returns>The interface types.</returns>
    private static List<Type> AgentContracts()
    {
        return typeof(IAgentSystemClient).Assembly.GetTypes()
            .Where(
                type => { return type.IsInterface && type.Namespace == "Maran.Agent.Client.Interfaces"; })
            .Where(type => { return type.Name.StartsWith("IAgent", StringComparison.Ordinal); })
            .Where(type => { return type.Name.EndsWith("Client", StringComparison.Ordinal); })
            .ToList();
    }

    /// <summary>Builds a container with the clock, the host's own registrations, then the simulated agent.</summary>
    /// <param name="before">Registers whatever the host adds before the simulated agent (its own caps).</param>
    /// <param name="after">Registers whatever the host adds after it (its own seeds).</param>
    /// <returns>The built container.</returns>
    private static ServiceProvider NewProvider(
        Action<IServiceCollection> before,
        Action<IServiceCollection>? after = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new FixedClock());
        before(services);
        services.AddSimulatedAgent();
        after?.Invoke(services);

        return services.BuildServiceProvider();
    }
}
