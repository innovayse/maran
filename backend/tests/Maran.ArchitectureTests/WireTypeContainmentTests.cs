using System.Reflection;
using NetArchTest.Rules;

namespace Maran.ArchitectureTests;

/// <summary>
/// Keeps the generated protobuf types inside the agent client's invoker layer (rules/security.md
/// item 8, "secrets never in logs").
/// </summary>
/// <remarks>
/// The reason this is a boundary rather than a convention. A generated request message —
/// <c>CreateDatabaseRequest</c> is the sharpest example — is <c>sealed partial</c>, and its
/// <c>ToString()</c> is protobuf's <c>ToDiagnosticString</c>, which prints every set field. For a
/// create-database or create-SFTP-user request that is the customer's password in full, and the
/// panel shows that password exactly once. Because <c>ToString</c> is already overridden in the
/// generated code, a <c>partial</c> of ours cannot replace it, and the <c>SensitiveString</c>
/// wrapper that defends every hand-written carrier cannot defend this path at all: by the time a
/// value is a field of a wire message it is a plain <c>string</c> again.
///
/// So the containment is structural. A wire message may be built, sent and read only where the
/// call is actually made; no module, no handler, no controller and nothing in the host may hold
/// one. A type that cannot leave the room cannot be logged from another room — and one
/// <c>logger.LogDebug("{Request}", request)</c> in a module is all it would take.
///
/// The room is three namespaces, not one, because that is what the invoker layer is made of:
/// the per-service clients that build the requests, the invoker interfaces that are the seam
/// between a client and the transport, and the error translator that reads the failure branch off
/// the wire. Everything else in the product is outside it.
///
/// The whole generated namespace is banned rather than only the message types. Nothing outside the
/// invoker layer has business with a generated enum or a gRPC stub either, and a rule that has to
/// tell a message from its neighbours is a rule that goes wrong quietly when the generator changes
/// what it emits.
/// </remarks>
public sealed class WireTypeContainmentTests
{
    /// <summary>The namespace the protobuf compiler puts every generated agent type in.</summary>
    private const string GeneratedNamespace = "Maran.Agent.V1";

    /// <summary>The namespaces a generated type may appear in — the invoker layer, and itself.</summary>
    private static readonly string[] TheRoom =
    [
        GeneratedNamespace,
        "Maran.Agent.Client.Services",
        "Maran.Agent.Client.Interfaces",
        "Maran.Agent.Client.Errors",
    ];

    /// <summary>Generated wire types never leave the agent client's invoker layer.</summary>
    [Fact]
    public void Generated_wire_types_never_leave_the_agent_clients_invoker_layer()
    {
        var assemblies = ProductAssemblies();

        // Vacuity guard, and the reason it is first: this rule searches for a dependency, and an
        // assembly set that does not contain the code under discussion produces "no violations"
        // — which reads exactly like a rule being honoured.
        Assert.Contains("Maran.Agent.Client", assemblies.Select(assembly =>
        {
            return assembly.GetName().Name;
        }));

        var violations = new List<string>();
        foreach (var assembly in assemblies)
        {
            var outsideTheRoom = Types.InAssembly(assembly)
                .That().DoNotResideInNamespaceStartingWith(TheRoom[0])
                .And().DoNotResideInNamespaceStartingWith(TheRoom[1])
                .And().DoNotResideInNamespaceStartingWith(TheRoom[2])
                .And().DoNotResideInNamespaceStartingWith(TheRoom[3])
                .ShouldNot().HaveDependencyOn(GeneratedNamespace)
                .GetResult();

            if (!outsideTheRoom.IsSuccessful)
            {
                violations.AddRange((outsideTheRoom.FailingTypeNames ?? []).Select(name =>
                {
                    return $"{name} (in {assembly.GetName().Name})";
                }));
            }
        }

        Assert.True(
            violations.Count == 0,
            $"A generated protobuf type prints every field it holds — including a password — from its "
            + $"own ToString, which no wrapper of ours can override. It may therefore be used only "
            + $"inside {string.Join(", ", TheRoom.Skip(1))}. Take what the caller needs off the "
            + $"message inside the client and hand out a hand-written type instead: "
            + string.Join("; ", violations));
    }

    /// <summary>The containment rule is searching a dependency it can actually find.</summary>
    /// <remarks>
    /// Without this, a NetArchTest search that silently matched nothing — a renamed generated
    /// namespace, a dependency form the search does not see — would report the product clean. The
    /// invoker layer is known to depend on the generated types on every call, so finding it there
    /// is what makes "found nowhere else" mean something.
    /// </remarks>
    [Fact]
    public void The_invoker_layer_itself_does_depend_on_the_generated_types()
    {
        var client = ProductAssemblies().Single(assembly =>
        {
            return string.Equals(assembly.GetName().Name, "Maran.Agent.Client", StringComparison.Ordinal);
        });

        // Asked as the negative form the containment rule itself uses, so this proves that exact
        // search finds something rather than proving that a different one would. Most types in the
        // invoker layer are hand-written result carriers that touch no wire type at all, so
        // "every type here depends on it" would be false for a healthy tree; what must hold is
        // that the search reports SOME type, which is a failing negative.
        var searched = Types.InAssembly(client)
            .That().ResideInNamespaceStartingWith("Maran.Agent.Client.Services")
            .ShouldNot().HaveDependencyOn(GeneratedNamespace)
            .GetResult();

        Assert.False(
            searched.IsSuccessful,
            "Every agent client builds a generated request, so a search that finds none inside the "
            + "invoker layer is broken rather than clean — and the containment rule beside this one "
            + "would then report the whole product clean for the same reason.");
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
                    // "Tests", not ".Tests": this project is Maran.ArchitectureTests, and it names
                    // the banned namespace in its own assertions.
                    && !name.EndsWith("Tests", StringComparison.Ordinal);
            })
            .Distinct()
            .ToList();
    }
}
