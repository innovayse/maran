using Grpc.Net.Client;
using Maran.Agent.Client.Interfaces;

namespace Maran.Agent.Client.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentChannels"/> a test steers by hand: the channel it hands out is whichever one
/// <see cref="Current"/> holds at the moment <see cref="ForCurrent"/> is asked, and every ask is counted.
/// </summary>
/// <remarks>
/// It stands where the routing pool stands in production, so a test can aim consecutive calls at
/// different doubles and prove each call read the channel when it started, not when its invoker was built.
/// </remarks>
internal sealed class SwitchableAgentChannels : IAgentChannels
{
    /// <summary>How many times <see cref="ForCurrent"/> has been asked.</summary>
    private int _requests;

    /// <summary>The channel the next call is sent over; the test replaces it between calls.</summary>
    public GrpcChannel Current { get; set; }

    /// <summary>How many times a channel has been requested since the instance was created.</summary>
    public int Requests
    {
        get { return Volatile.Read(ref _requests); }
    }

    /// <summary>Creates the instance aimed at <paramref name="initial"/>.</summary>
    /// <param name="initial">The channel the first call is sent over.</param>
    public SwitchableAgentChannels(GrpcChannel initial)
    {
        Current = initial;
    }

    /// <inheritdoc/>
    public GrpcChannel ForCurrent()
    {
        Interlocked.Increment(ref _requests);
        return Current;
    }

    /// <inheritdoc/>
    public GrpcChannel ForServer(Guid serverId)
    {
        return ForCurrent();
    }
}
