using System.Reflection;
using NetArchTest.Rules;

namespace Maran.ArchitectureTests;

/// <summary>
/// Keeps the gRPC transport's failure type inside the two places that translate it, so that an
/// unreachable agent is answered by one mapping and cannot be swallowed or mis-answered elsewhere.
/// </summary>
/// <remarks>
/// A transport failure — the agent's socket missing, refused, or too slow — is an
/// <c>RpcException</c> raised deep inside a call. The panel answers it as an unavailable service
/// (HTTP 503) through the translation in <c>Maran.Agent.Client</c>, and retries it through the
/// resilience pipelines in <c>Maran.Host.Resilience</c>. A module, a handler or a health probe that
/// names the gRPC namespace is catching the failure on its own, and a catch written there either
/// swallows the outage into a success-shaped answer or reports it as a server bug. The rule
/// therefore says where the type may be seen: the generated stubs, the agent client and the
/// resilience pipelines. Everything else reads a <c>Result</c>.
/// </remarks>
public sealed class TransportExceptionContainmentTests
{
    /// <summary>The namespace of the gRPC core types, home of the transport exception.</summary>
    private const string TransportNamespace = "Grpc.Core";

    /// <summary>The namespaces allowed to depend on the transport namespace.</summary>
    private static readonly string[] TheRoom =
    [
        "Maran.Agent.V1",
        "Maran.Agent.Client",
        "Maran.Host.Resilience",
    ];

    /// <summary>No type outside the translation and resilience layers depends on the gRPC namespace.</summary>
    [Fact]
    public void The_transport_exception_is_not_visible_outside_the_translation_and_resilience_layers()
    {
        var assemblies = ProductAssemblies();

        // Vacuity guard: the search must be judged over the assemblies that hold the code at all.
        Assert.Contains("Maran.Host", assemblies.Select(assembly =>
        {
            return assembly.GetName().Name;
        }));

        var violations = new List<string>();
        foreach (var assembly in assemblies)
        {
            var outside = Types.InAssembly(assembly)
                .That().DoNotResideInNamespaceStartingWith(TheRoom[0])
                .And().DoNotResideInNamespaceStartingWith(TheRoom[1])
                .And().DoNotResideInNamespaceStartingWith(TheRoom[2])
                .ShouldNot().HaveDependencyOn(TransportNamespace)
                .GetResult();

            if (!outside.IsSuccessful)
            {
                violations.AddRange((outside.FailingTypeNames ?? []).Select(name =>
                {
                    return $"{name} (in {assembly.GetName().Name})";
                }));
            }
        }

        Assert.True(
            violations.Count == 0,
            $"The transport exception is mapped to an unavailable answer in {TheRoom[1]} and retried "
            + $"in {TheRoom[2]}; a catch anywhere else either hides the outage or reports it as a "
            + "server bug. Read the Result the client returns instead: "
            + string.Join("; ", violations));
    }

    /// <summary>The containment search finds the dependency where it is known to live.</summary>
    /// <remarks>
    /// Both permitted layers raise or handle the transport exception on every call, so the same
    /// negative search used by the rule must report them. Without this a search that matched
    /// nothing would report the product clean.
    /// </remarks>
    [Theory]
    [InlineData("Maran.Agent.Client", "Maran.Agent.Client")]
    [InlineData("Maran.Host", "Maran.Host.Resilience")]
    public void The_permitted_layers_do_depend_on_the_transport_namespace(string assemblyName, string layer)
    {
        var assembly = ProductAssemblies().Single(candidate =>
        {
            return string.Equals(candidate.GetName().Name, assemblyName, StringComparison.Ordinal);
        });

        var searched = Types.InAssembly(assembly)
            .That().ResideInNamespaceStartingWith(layer)
            .ShouldNot().HaveDependencyOn(TransportNamespace)
            .GetResult();

        Assert.False(
            searched.IsSuccessful,
            $"{layer} translates or retries the transport exception, so a search that finds no "
            + "dependency there is broken rather than clean.");
        Assert.NotEmpty(searched.FailingTypeNames ?? []);
    }

    /// <summary>
    /// Every product assembly beside the test binary, test projects excluded.
    /// </summary>
    /// <returns>The assemblies the containment rule is judged over.</returns>
    private static List<Assembly> ProductAssemblies()
    {
        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "Maran.*.dll"))
        {
            try
            {
                Assembly.LoadFrom(path);
            }
            catch (BadImageFormatException)
            {
                // Native or mixed-mode files matching the pattern are not managed assemblies.
            }
        }

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly =>
            {
                var name = assembly.GetName().Name;
                return name is not null
                    && name.StartsWith("Maran", StringComparison.Ordinal)
                    && !name.EndsWith("Tests", StringComparison.Ordinal);
            })
            .Distinct()
            .ToList();
    }
}
