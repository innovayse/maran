using System.Reflection;
using Maran.Agent.Client.Interfaces;
using Maran.ArchitectureTests.Fixtures;
using Maran.Host.Modules;
using Maran.Sdk.Contracts;

namespace Maran.ArchitectureTests;

/// <summary>
/// Makes a module's reach into the agent a declared, checked fact rather than whatever its code
/// happens to inject.
/// </summary>
/// <remarks>
/// The guard itself runs when the panel composes its modules, so a violation cannot boot. These
/// tests run it in CI as well, because a rule whose only enforcement is a startup crash is a rule
/// discovered by an operator rather than by the person who broke it.
/// </remarks>
public sealed class AgentCapabilityTests
{
    /// <summary>Every composed module declares each part of the agent it depends on.</summary>
    [Fact]
    public void Every_module_declares_the_agent_it_uses()
    {
        AgentCapabilityGuard.Verify(ModuleRegistry.All);
    }

    /// <summary>A module that takes an agent client without declaring it is refused.</summary>
    /// <remarks>
    /// The positive control, and the assertion above is worthless without it: every real module is
    /// honest, so a guard that had silently stopped inspecting anything would pass that test
    /// unchanged. <see cref="UndeclaredAgentModule"/> is the value this probe must find, and the
    /// message is asserted too — a refusal that did not name the module or the capability would
    /// leave an operator with a panel that will not start and no idea which module to remove.
    /// </remarks>
    [Fact]
    public void A_module_that_hides_a_capability_is_refused()
    {
        var refused = Assert.Throws<InvalidOperationException>(() =>
        {
            AgentCapabilityGuard.Verify([new UndeclaredAgentModule(null!)]);
        });

        Assert.Contains("undeclared", refused.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(AgentCapability.Sites), refused.Message, StringComparison.Ordinal);
    }

    /// <summary>Every agent client contract has a capability that names it.</summary>
    /// <remarks>
    /// The gap this closes is the one a new agent service opens. A client interface the enum cannot
    /// name is a door no module has to declare, and the guard would throw about it at startup — on
    /// a customer's server, at the worst moment. Here it fails in CI, in the pull request that added
    /// the service.
    /// </remarks>
    [Fact]
    public void Every_agent_client_has_a_capability()
    {
        var clients = typeof(IAgentSitesClient).Assembly.GetTypes()
            .Where(type =>
            {
                return type.IsInterface
                    && type.IsPublic
                    && string.Equals(type.Namespace, "Maran.Agent.Client.Interfaces", StringComparison.Ordinal)
                    && type.Name.StartsWith("IAgent", StringComparison.Ordinal)
                    && type.Name.EndsWith("Client", StringComparison.Ordinal);
            })
            .ToList();

        Assert.True(clients.Count >= 10, $"Only {clients.Count} agent client contracts were found");

        var unnamed = clients
            .Select(type => { return type.Name["IAgent".Length..^"Client".Length]; })
            .Where(middle => { return !Enum.TryParse<AgentCapability>(middle, ignoreCase: false, out _); })
            .ToList();

        Assert.True(
            unnamed.Count == 0,
            $"Agent client contracts no AgentCapability names: {string.Join(", ", unnamed)}. "
            + "Add a value for each, or modules never have to declare that part of the agent.");
    }

    /// <summary>No module declares a capability it does not use.</summary>
    /// <remarks>
    /// The other direction, and it is not pedantry: the manifest is what an administrator reads
    /// before granting a module root, so a capability listed and never used overstates what the
    /// module needs and trains the reader to skim the list.
    /// </remarks>
    [Fact]
    public void No_module_declares_a_capability_it_does_not_use()
    {
        var overstated = ModuleRegistry.All
            .SelectMany(module =>
            {
                var used = UsedCapabilities(module.GetType().Assembly);
                return module.Manifest.AgentCapabilities
                    .Where(capability => { return !used.Contains(capability); })
                    .Select(capability => { return $"{module.Manifest.Id} declares {capability}"; });
            })
            .OrderBy(entry => { return entry; }, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            overstated.Count == 0,
            $"Manifests claiming agent access their module never takes: {string.Join("; ", overstated)}");
    }

    /// <summary>The agent capabilities an assembly's declared dependencies actually reach for.</summary>
    /// <param name="assembly">The module assembly to inspect.</param>
    /// <returns>The distinct capabilities its type signatures name.</returns>
    private static HashSet<AgentCapability> UsedCapabilities(Assembly assembly)
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        return assembly.GetTypes()
            .SelectMany(type =>
            {
                return type.GetConstructors(All).Cast<MethodBase>()
                    .Concat(type.GetMethods(All))
                    .SelectMany(method => { return method.GetParameters(); })
                    .Select(parameter => { return parameter.ParameterType; })
                    .Concat(type.GetFields(All).Select(field => { return field.FieldType; }))
                    .Concat(type.GetProperties(All).Select(property => { return property.PropertyType; }));
            })
            .Where(type =>
            {
                return type.IsInterface
                    && string.Equals(type.Namespace, "Maran.Agent.Client.Interfaces", StringComparison.Ordinal)
                    && type.Name.StartsWith("IAgent", StringComparison.Ordinal)
                    && type.Name.EndsWith("Client", StringComparison.Ordinal);
            })
            .Select(type => { return Enum.Parse<AgentCapability>(type.Name["IAgent".Length..^"Client".Length]); })
            .ToHashSet();
    }
}
