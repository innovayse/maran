using System.Reflection;
using System.Text.RegularExpressions;
using Maran.Agent.Client.Interfaces;
using Maran.Host.Modules;

namespace Maran.ArchitectureTests;

/// <summary>
/// Holds the public demonstration's composition root to the product's: the demo calls everything the
/// Host calls except the real agent's registration and decoration, and the Host never references the
/// demonstration or its simulated agent.
/// </summary>
/// <remarks>
/// <para>
/// The first half is a source comparison of the two <c>Program.cs</c> tables of contents, because the
/// property is "nothing the Host does was silently dropped", and a dropped call compiles. The second is an
/// assembly-reference search, which is only worth anything with a control: the demo assembly is asked the
/// same question and must answer yes, so a search that had stopped looking would fail here and not pass.
/// </para>
/// <para>
/// The third is a constructor census: the demo registers no agent routing (the simulated clients take
/// no channel), so a composed type that asks for <c>IAgentTarget</c> or <c>IAgentChannels</c> would fail
/// to resolve there and nowhere else. The census fails first, naming the type, so the routing pieces are
/// registered in the demo in the same change that introduces the consumer.
/// </para>
/// <para>
/// UNOBSERVED HERE: a consumer that reaches routing through a service-location call or a factory lambda
/// rather than a constructor parameter; and that the shipped release bundle contains neither assembly. That is a property of the
/// bundle, which only <c>maran release</c> builds, and no test in this project opens one.
/// </para>
/// </remarks>
public sealed class DemoCompositionTests
{
    /// <summary>The calls the demo replaces on purpose: the real agent's registration and its decoration.</summary>
    private static readonly string[] ReplacedCalls = ["AddAgentClient", "AddResilientAgentClients"];

    /// <summary>The calls only the demonstration makes.</summary>
    private static readonly string[] DemoOnlyCalls =
        ["AddSimulatedAgent", "AddDemoGuard", "AddDemoWorld", "UseClientPseudonym", "UseDemoWriteGuard"];

    /// <summary>The names of the two assemblies the Host must never reference.</summary>
    private static readonly string[] ForbiddenReferences = ["Maran.Host.Demo", "Maran.Agent.Simulated"];

    /// <summary>The demo composes every call the Host does, except the real agent's pair.</summary>
    [Fact]
    public void The_demo_makes_every_host_call_except_the_real_agent_pair()
    {
        var host = CallsIn(ReadProgram("Maran.Host"));
        var demo = CallsIn(ReadProgram("Maran.Host.Demo"));

        Assert.Empty(MissingFrom(host, demo, ReplacedCalls));
    }

    /// <summary>The demo registers its own agent and guard, and registers the real agent's pair nowhere.</summary>
    [Fact]
    public void The_demo_composes_its_own_agent_and_never_the_real_one()
    {
        var demo = CallsIn(ReadProgram("Maran.Host.Demo"));

        Assert.All(DemoOnlyCalls, call =>
        {
            Assert.Contains(call, demo);
        });
        Assert.All(ReplacedCalls, call =>
        {
            Assert.DoesNotContain(call, demo);
        });
    }

    /// <summary>Control: the search finds the real agent's registration in the Host, so a miss in the demo means something.</summary>
    [Fact]
    public void Control_the_host_does_register_the_real_agent()
    {
        var host = CallsIn(ReadProgram("Maran.Host"));

        Assert.All(ReplacedCalls, call =>
        {
            Assert.Contains(call, host);
        });
    }

    /// <summary>Control: a composition that leaves out a Host call is reported, so the comparison can refuse.</summary>
    [Fact]
    public void Control_a_planted_composition_missing_a_call_is_refused()
    {
        var host = CallsIn("builder.Services.AddPanelSecurity();\nbuilder.Services.AddAgentClient(x);");
        var planted = CallsIn("builder.Services.AddSimulatedAgent();");

        Assert.Equal(["AddPanelSecurity"], MissingFrom(host, planted, ReplacedCalls));
    }

    /// <summary>The Host references neither the demonstration nor its simulated agent.</summary>
    [Fact]
    public void The_host_references_neither_the_demo_nor_the_simulated_agent()
    {
        var referenced = typeof(Maran.Host.Program).Assembly.GetReferencedAssemblies()
            .Select(name => { return name.Name ?? string.Empty; })
            .ToList();

        Assert.All(ForbiddenReferences, name =>
        {
            Assert.DoesNotContain(name, referenced);
        });
    }

    /// <summary>Control: the demo assembly does reference both, so the reference search is capable of a hit.</summary>
    [Fact]
    public void Control_the_demo_assembly_references_the_host_and_the_simulated_agent()
    {
        var referenced = typeof(Maran.Host.Demo.Program).Assembly.GetReferencedAssemblies()
            .Select(name => { return name.Name ?? string.Empty; })
            .ToList();

        Assert.Contains("Maran.Host", referenced);
        Assert.Contains("Maran.Agent.Simulated", referenced);
    }

    /// <summary>No composed module or Host type asks for the agent routing the demo does not register.</summary>
    [Fact]
    public void No_composed_type_asks_for_agent_routing_the_demo_does_not_register()
    {
        var assemblies = ModuleRegistry.All
            .Select(module => { return module.GetType().Assembly; })
            .Append(typeof(Maran.Host.Program).Assembly)
            .Distinct()
            .ToList();

        Assert.Empty(RoutingConsumers(assemblies));
    }

    /// <summary>Control: the census finds the real agent clients, which do take the channels, so an empty answer means something.</summary>
    [Fact]
    public void Control_the_census_finds_the_real_agent_clients_asking_for_channels()
    {
        var consumers = RoutingConsumers([typeof(IAgentChannels).Assembly]);

        Assert.Contains("Maran.Agent.Client.Services.AccountsService.AgentAccountsClient", consumers);
    }

    /// <summary>The composition calls a <c>Program.cs</c> text makes on the builder's services, host and the app.</summary>
    /// <param name="source">The text of one <c>Program.cs</c>.</param>
    /// <returns>The distinct method names called.</returns>
    private static HashSet<string> CallsIn(string source)
    {
        var calls = Regex.Matches(source, @"(?:builder\.Services|builder\.Host|app)\.(\w+)\(");

        return calls.Select(match => { return match.Groups[1].Value; }).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The types whose constructors ask for <c>IAgentTarget</c> or <c>IAgentChannels</c>.</summary>
    /// <param name="assemblies">The assemblies to walk.</param>
    /// <returns>The full type names, sorted.</returns>
    private static List<string> RoutingConsumers(IEnumerable<Assembly> assemblies)
    {
        const BindingFlags AllConstructors = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        return assemblies
            .SelectMany(assembly => { return assembly.GetTypes(); })
            .Where(type =>
            {
                return type.GetConstructors(AllConstructors).Any(constructor =>
                {
                    return constructor.GetParameters().Any(parameter =>
                    {
                        return parameter.ParameterType == typeof(IAgentTarget)
                            || parameter.ParameterType == typeof(IAgentChannels);
                    });
                });
            })
            .Select(type => { return type.FullName ?? type.Name; })
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The Host calls the demo lacks, not counting the ones it replaces on purpose.</summary>
    /// <param name="host">What the Host calls.</param>
    /// <param name="demo">What the demo calls.</param>
    /// <param name="replaced">The calls the demo is allowed to lack.</param>
    /// <returns>The missing call names, sorted.</returns>
    private static List<string> MissingFrom(HashSet<string> host, HashSet<string> demo, string[] replaced)
    {
        return host
            .Where(call => { return !demo.Contains(call) && !replaced.Contains(call); })
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Reads one project's <c>Program.cs</c>, found by walking up from the test binary.</summary>
    /// <param name="project">The project folder name under <c>backend/src</c>.</param>
    /// <returns>The text of the file.</returns>
    private static string ReadProgram(string project)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", project, "Program.cs");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"backend/src/{project}/Program.cs was not found above the test binary.");
    }
}
