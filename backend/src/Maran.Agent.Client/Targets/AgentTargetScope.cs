namespace Maran.Agent.Client.Targets;

/// <summary>
/// An open <see cref="Interfaces.IAgentTarget.Begin"/> scope: puts the previous target back when it
/// is disposed.
/// </summary>
internal sealed class AgentTargetScope : IDisposable
{
    /// <summary>The slot the owning target keeps its current server in.</summary>
    private readonly AsyncLocal<Guid?> _slot;

    /// <summary>The value the slot held when the scope was opened.</summary>
    private readonly Guid? _previous;

    /// <summary>Whether the previous value has been put back already.</summary>
    private bool _disposed;

    /// <summary>Opens a scope over <paramref name="slot"/>.</summary>
    /// <param name="slot">The slot the owning target keeps its current server in.</param>
    /// <param name="previous">What the slot held before the scope changed it.</param>
    public AgentTargetScope(AsyncLocal<Guid?> slot, Guid? previous)
    {
        _slot = slot;
        _previous = previous;
    }

    /// <summary>Restores the previous target; a second call does nothing.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _slot.Value = _previous;
    }
}
