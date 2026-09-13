using System.Reflection;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Modules;

/// <summary>
/// Refuses to compose a module that reaches for a part of the agent it did not declare in its
/// <see cref="Manifest"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The hole this closes.</b> <c>Maran.Agent.Client</c> is one door to the only root process on
/// the server, and every module runs in the same process behind it. Nothing stopped a module from
/// resolving <c>IAgentFirewallClient</c> and opening a port, whatever it said it was for. That is a
/// review problem while every module is written here, and an unanswerable one the day a module is
/// bought from a marketplace: the administrator installs a backups module and grants it root, with
/// only its description as evidence of what it will touch.
/// </para>
/// <para>
/// <b>Why the check is at composition and not at the call.</b> A check at the call site would have
/// to answer "who is calling", and the honest ways to do that are expensive or wrong — walking the
/// stack breaks under inlining and async state machines, and ambient state has to be set by
/// something the caller could avoid. A module's DEPENDENCIES, by contrast, are in its metadata: it
/// cannot obtain a client the container does not hand it, and what the container hands it is
/// declared in the signatures this reads. So the answer is available before the first request, it
/// is exact, and a module that fails it does not load at all rather than failing halfway through an
/// operation on a customer's server.
/// </para>
/// <para>
/// <b>Stated blind spot.</b> A module that takes <see cref="IServiceProvider"/> and asks it for a
/// client at runtime declares nothing in its metadata and is not caught here. That is a deliberate
/// limit rather than an oversight: closing it means not registering the agent clients in the shared
/// container at all, which is a larger change than this guard, and service location is itself
/// already a review reject in this repository. What this guard makes true is that the ORDINARY way
/// to reach the agent — the only one any module in the tree uses — is declared and enforced.
/// </para>
/// <para>
/// <b>Why the mapping is derived rather than tabulated.</b> The capability for a client is its own
/// name (<c>IAgentSitesClient</c> ⇒ <see cref="AgentCapability.Sites"/>). A table would be a second
/// place to forget: a new agent service added to the client with no value in the enum would map to
/// nothing, and "maps to nothing" would quietly mean "needs no capability". Here it throws.
/// </para>
/// </remarks>
public static class AgentCapabilityGuard
{
    /// <summary>The namespace every agent client contract lives in.</summary>
    private const string ClientNamespace = "Maran.Agent.Client.Interfaces";

    /// <summary>What an agent client interface's name starts with.</summary>
    private const string ClientPrefix = "IAgent";

    /// <summary>What an agent client interface's name ends with.</summary>
    private const string ClientSuffix = "Client";

    /// <summary>Verifies every module's declared capabilities cover the agent clients it depends on.</summary>
    /// <param name="modules">The modules about to be composed.</param>
    /// <exception cref="InvalidOperationException">
    /// A module depends on an agent client outside its declared capabilities, or on one this panel
    /// cannot name a capability for. Thrown rather than logged: composing it anyway would grant the
    /// access the declaration exists to withhold.
    /// </exception>
    public static void Verify(IEnumerable<IPanelModule> modules)
    {
        var refusals = modules
            .SelectMany(Refusals)
            .OrderBy(refusal => { return refusal; }, StringComparer.Ordinal)
            .ToList();

        if (refusals.Count > 0)
        {
            throw new InvalidOperationException(
                "These modules reach parts of the agent their manifest does not declare: "
                + string.Join("; ", refusals)
                + ". Add the capability to the module's Manifest, or stop depending on the client.");
        }
    }

    /// <summary>Lists one module's undeclared agent dependencies.</summary>
    /// <param name="module">The module to inspect.</param>
    /// <returns>One sentence per undeclared capability, or nothing when the module is honest.</returns>
    private static IEnumerable<string> Refusals(IPanelModule module)
    {
        var declared = module.Manifest.AgentCapabilities.ToHashSet();

        return RequiredCapabilities(module.GetType().Assembly)
            .Where(capability => { return !declared.Contains(capability); })
            .Distinct()
            .Select(capability => { return $"{module.Manifest.Id} uses {capability}"; });
    }

    /// <summary>Reads the agent capabilities an assembly's declared dependencies require.</summary>
    /// <param name="assembly">The module assembly to inspect.</param>
    /// <returns>Every capability its type signatures reach for, with repeats.</returns>
    private static IEnumerable<AgentCapability> RequiredCapabilities(Assembly assembly)
    {
        return assembly.GetTypes()
            .SelectMany(ReferencedTypes)
            .Where(IsAgentClient)
            .Select(CapabilityOf);
    }

    /// <summary>Every type one module type names in its own signatures.</summary>
    /// <param name="type">The type to read.</param>
    /// <returns>Constructor and method parameter types, field types and property types.</returns>
    /// <remarks>
    /// Signatures and not IL bodies. A client can only come from the container, and the container
    /// only fills what a signature asks for, so a dependency that never appears in a signature is a
    /// dependency the module did not obtain the ordinary way — see the type's stated blind spot.
    /// </remarks>
    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var fromParameters = type.GetConstructors(All).Cast<MethodBase>()
            .Concat(type.GetMethods(All))
            .SelectMany(method => { return method.GetParameters(); })
            .Select(parameter => { return parameter.ParameterType; });

        var fromFields = type.GetFields(All).Select(field => { return field.FieldType; });
        var fromProperties = type.GetProperties(All).Select(property => { return property.PropertyType; });

        return fromParameters.Concat(fromFields).Concat(fromProperties);
    }

    /// <summary>Whether a type is one of the agent client contracts.</summary>
    /// <param name="type">The type to judge.</param>
    /// <returns><c>true</c> when it is an <c>IAgent*Client</c> from the agent client's contracts namespace.</returns>
    private static bool IsAgentClient(Type type)
    {
        return type.IsInterface
            && string.Equals(type.Namespace, ClientNamespace, StringComparison.Ordinal)
            && type.Name.StartsWith(ClientPrefix, StringComparison.Ordinal)
            && type.Name.EndsWith(ClientSuffix, StringComparison.Ordinal);
    }

    /// <summary>Names the capability one client interface grants.</summary>
    /// <param name="clientType">The client interface, e.g. <c>IAgentSitesClient</c>.</param>
    /// <returns>The matching <see cref="AgentCapability"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// The interface's name matches no capability. Thrown rather than skipped: a new agent service
    /// with no value in the enum would otherwise be the one part of the agent no module has to
    /// declare, which is the opposite of what this guard is for.
    /// </exception>
    private static AgentCapability CapabilityOf(Type clientType)
    {
        var middle = clientType.Name[ClientPrefix.Length..^ClientSuffix.Length];

        if (!Enum.TryParse<AgentCapability>(middle, ignoreCase: false, out var capability))
        {
            throw new InvalidOperationException(
                $"{clientType.Name} grants access to the agent and no AgentCapability names it. "
                + $"Add '{middle}' to AgentCapability so modules must declare it.");
        }

        return capability;
    }
}
