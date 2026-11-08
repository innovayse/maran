using System.Reflection;
using Grpc.Core;
using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Tests.Routing;

/// <summary>
/// No agent service is left aimed at one fixed channel: every invoker and every public client takes
/// routed channels, so a service added later cannot quietly ignore the selected server.
/// </summary>
public sealed class AgentClientRoutingCensusTests
{
    /// <summary>The assembly that holds every invoker and client.</summary>
    private static readonly Assembly ClientAssembly = typeof(AgentChannel).Assembly;

    /// <summary>The census covers every client contract: one invoker per contract, and none is skipped.</summary>
    /// <remarks>Guards the other tests in this class against passing over an empty list.</remarks>
    [Fact]
    public void There_is_one_invoker_and_one_client_for_every_client_contract()
    {
        var contracts = ClientAssembly.GetTypes().Count(type =>
        {
            return type.IsInterface && IsClientContract(type);
        });

        Assert.True(contracts > 0);
        Assert.Equal(contracts, Invokers().Count);
        Assert.Equal(contracts, Clients().Count);
    }

    /// <summary>Every invoker is built from routed channels and keeps no generated client.</summary>
    [Fact]
    public void Every_invoker_takes_routed_channels_and_holds_no_generated_client()
    {
        foreach (var invoker in Invokers())
        {
            var constructors = invoker.GetConstructors();
            var constructor = Assert.Single(constructors);

            Assert.Equal(
                new[] { typeof(IAgentChannels) },
                constructor.GetParameters().Select(parameter =>
                {
                    return parameter.ParameterType;
                }));
            Assert.False(HoldsAGeneratedClient(invoker), $"{invoker.Name} keeps a generated client");
        }
    }

    /// <summary>Every public client can be built from routed channels.</summary>
    [Fact]
    public void Every_public_client_has_a_constructor_taking_routed_channels()
    {
        foreach (var client in Clients())
        {
            var found = client.GetConstructors().Any(constructor =>
            {
                var parameters = constructor.GetParameters();
                return parameters.Length == 2
                    && parameters[0].ParameterType == typeof(IAgentChannels)
                    && parameters[1].ParameterType.IsGenericType
                    && parameters[1].ParameterType.GetGenericTypeDefinition() == typeof(ILogger<>);
            });

            Assert.True(found, $"{client.Name} has no (IAgentChannels, ILogger) constructor");
        }
    }

    /// <summary>The detector refuses a type that keeps a generated client, so the census above can fail.</summary>
    /// <remarks>The inverse control: the same predicate must accept the shape the invokers now have.</remarks>
    [Fact]
    public void The_detector_flags_a_type_holding_a_generated_client_and_accepts_one_that_does_not()
    {
        var fixedChannel = new PlantedFixedChannelInvoker(null);
        var routed = new PlantedRoutedInvoker(null);

        Assert.True(HoldsAGeneratedClient(fixedChannel.GetType()));
        Assert.False(HoldsAGeneratedClient(routed.GetType()));
        Assert.Null(fixedChannel.Read());
        Assert.Null(routed.Read());
    }

    /// <summary>The production invokers: the types named <c>Grpc&lt;Service&gt;ServiceInvoker</c>.</summary>
    /// <returns>Every such type in the assembly.</returns>
    private static List<Type> Invokers()
    {
        return ClientAssembly.GetTypes()
            .Where(type =>
            {
                return type.Name.StartsWith("Grpc", StringComparison.Ordinal)
                                && type.Name.EndsWith("ServiceInvoker", StringComparison.Ordinal);
            })
            .ToList();
    }

    /// <summary>The public clients: the types that implement a public <c>IAgent&lt;Service&gt;Client</c> contract.</summary>
    /// <returns>Every such type in the assembly.</returns>
    private static List<Type> Clients()
    {
        return ClientAssembly.GetTypes()
            .Where(type =>
            {
                return type.IsClass && type.IsPublic && type.Name.StartsWith("Agent", StringComparison.Ordinal)
                                && type.GetInterfaces().Any(IsClientContract);
            })
            .ToList();
    }

    /// <summary>Whether <paramref name="type"/> is one of the public <c>IAgent*Client</c> contracts.</summary>
    /// <param name="type">The interface to judge.</param>
    /// <returns><c>true</c> for a client contract.</returns>
    private static bool IsClientContract(Type type)
    {
        return type.Namespace == typeof(IAgentChannels).Namespace
            && type.Name.StartsWith("IAgent", StringComparison.Ordinal)
            && type.Name.EndsWith("Client", StringComparison.Ordinal);
    }

    /// <summary>Whether <paramref name="type"/> keeps a generated gRPC client, and with it one fixed channel.</summary>
    /// <param name="type">The invoker to judge.</param>
    /// <returns><c>true</c> when an instance field is a generated client.</returns>
    private static bool HoldsAGeneratedClient(Type type)
    {
        return type
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(field =>
            {
                return typeof(ClientBase).IsAssignableFrom(field.FieldType);
            });
    }

    /// <summary>A planted violation: an invoker shaped like the old one, bound to one generated client.</summary>
    private sealed class PlantedFixedChannelInvoker
    {
        /// <summary>The generated client that pins the invoker to one channel.</summary>
        private readonly Maran.Agent.V1.SystemService.SystemServiceClient? _client;

        /// <summary>Plants the invoker with the client it is bound to.</summary>
        /// <param name="client">The generated client, or none.</param>
        public PlantedFixedChannelInvoker(Maran.Agent.V1.SystemService.SystemServiceClient? client)
        {
            _client = client;
        }

        /// <summary>The field, read so the compiler keeps it.</summary>
        /// <returns>The client.</returns>
        public Maran.Agent.V1.SystemService.SystemServiceClient? Read()
        {
            return _client;
        }
    }

    /// <summary>The accepted shape: only routed channels are held.</summary>
    private sealed class PlantedRoutedInvoker
    {
        /// <summary>The routed channels.</summary>
        private readonly IAgentChannels? _channels;

        /// <summary>Plants the invoker with the channels it routes through.</summary>
        /// <param name="channels">The routed channels, or none.</param>
        public PlantedRoutedInvoker(IAgentChannels? channels)
        {
            _channels = channels;
        }

        /// <summary>The field, read so the compiler keeps it.</summary>
        /// <returns>The channels.</returns>
        public IAgentChannels? Read()
        {
            return _channels;
        }
    }
}
