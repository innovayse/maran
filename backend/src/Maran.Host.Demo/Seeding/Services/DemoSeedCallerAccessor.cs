using Maran.SharedKernel.Callers;

namespace Maran.Host.Demo.Seeding.Services;

/// <summary>
/// Holds the caller the seed is acting as, for the asynchronous flow that entered it. Every message
/// handler opens a scope of its own, so a scoped value could not reach them; an ambient value on the
/// flow does, and ends with it.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="Act"/> sets it, and only <see cref="DemoSeeder"/> and the protected-resource loader
/// call that. A web request never enters it, so on a served request <see cref="Current"/> is null and the
/// request's own caller stands. It is an instance field of a singleton, not static state.
/// </para>
/// <para>
/// <b>The ambient value is a scope object, not the caller.</b> Work started inside the scope (a task, a
/// timer, a pool work item) copies the ambient value and keeps it for its whole life, and restoring the
/// slot on dispose only reaches the flow that disposes. Holding the caller directly would leave every
/// such copy acting as the administrator after the scope ended. Holding a scope that
/// the scope's disposal deactivates means every copy reads <c>null</c> from then on, so
/// "a served request never acts as the seed" is a property of this type and not of today's call graph.
/// </para>
/// </remarks>
public sealed class DemoSeedCallerAccessor
{
    /// <summary>The scope of the current flow.</summary>
    private readonly AsyncLocal<Restoration?> _current = new();

    /// <summary>The caller the seed is acting as on this flow, or null when it is not seeding.</summary>
    public Caller? Current
    {
        get
        {
            var scope = _current.Value;
            return scope is { Active: true } ? scope.Caller : null;
        }
    }

    /// <summary>Acts as <paramref name="caller"/> until the returned scope is disposed.</summary>
    /// <param name="caller">Who the seed acts as.</param>
    /// <returns>
    /// A scope that restores the previous scope when disposed and ends the caller for every flow that copied it.
    /// </returns>
    public IDisposable Act(Caller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        var scope = new Restoration(_current, _current.Value, caller);
        _current.Value = scope;

        return scope;
    }

    /// <summary>One acting-as scope: the caller, whether it is still open, and the scope to put back.</summary>
    private sealed class Restoration : IDisposable
    {
        /// <summary>The slot to restore.</summary>
        private readonly AsyncLocal<Restoration?> _slot;

        /// <summary>The scope to put back.</summary>
        private readonly Restoration? _previous;

        /// <summary>Whether the scope is still open; read by flows that copied it.</summary>
        private volatile bool _active = true;

        /// <summary>Who the seed acts as inside this scope.</summary>
        public Caller Caller { get; }

        /// <summary>Whether the scope is still open.</summary>
        public bool Active
        {
            get { return _active; }
        }

        /// <summary>Creates the scope.</summary>
        /// <param name="slot">The slot to restore.</param>
        /// <param name="previous">The scope to put back.</param>
        /// <param name="caller">Who the seed acts as.</param>
        public Restoration(AsyncLocal<Restoration?> slot, Restoration? previous, Caller caller)
        {
            _slot = slot;
            _previous = previous;
            Caller = caller;
        }

        /// <summary>
        /// Ends the scope for every flow that copied it, and puts the previous scope back on this one.
        /// </summary>
        public void Dispose()
        {
            _active = false;
            _slot.Value = _previous;
        }
    }
}
