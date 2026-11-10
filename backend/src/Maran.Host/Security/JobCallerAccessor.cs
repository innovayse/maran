using Maran.SharedKernel.Callers;

namespace Maran.Host.Security;

/// <summary>
/// Holds the caller a durable job is acting as, for the asynchronous flow that entered it. Every message
/// handler opens a scope of its own, so a scoped value could not reach them; an ambient value on the flow does,
/// and ends with it.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="Act"/> sets it, and only <see cref="JobCallerScopes"/> calls that, after re-reading the
/// administrator. A web request never enters it, so on a served request <see cref="Current"/> is null and the
/// request's own caller stands. It is an instance field of a singleton, not static state.
/// </para>
/// <para>
/// <b>The ambient value is a scope object, not the caller.</b> Work started inside the scope (a task, a timer,
/// a pool work item) copies the ambient value and keeps it for its whole life, and restoring the slot on
/// dispose only reaches the flow that disposes. Holding the caller directly would leave every such copy acting
/// as the administrator after the scope ended. Holding a scope that its disposal deactivates means every copy
/// reads <c>null</c> from then on, so "a job never outlives its scope as an administrator" is a property of
/// this type and not of today's call graph.
/// </para>
/// </remarks>
public sealed class JobCallerAccessor
{
    /// <summary>The scope of the current flow.</summary>
    private readonly AsyncLocal<Restoration?> _current = new();

    /// <summary>The caller the job is acting as on this flow, or null when no job scope is open.</summary>
    public Caller? Current
    {
        get
        {
            for (var scope = _current.Value; scope is not null; scope = scope.Previous)
            {
                if (scope.Active)
                {
                    return scope.Caller;
                }
            }

            return null;
        }
    }

    /// <summary>Acts as <paramref name="caller"/> until the returned scope is disposed.</summary>
    /// <param name="caller">Who the job acts as.</param>
    /// <returns>A scope that restores the previous scope when disposed and ends the caller for every flow that copied it.</returns>
    public IDisposable Act(AdministratorCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        var scope = new Restoration(_current, _current.Value);
        scope.Bind(caller);
        _current.Value = scope;

        return scope;
    }

    /// <summary>
    /// Acts as the administrator <paramref name="resolve"/> finds, from the moment this method returns on the
    /// calling flow, or as nobody when it finds none.
    /// </summary>
    /// <remarks>
    /// Deliberately not an <c>async</c> method: a value set on an ambient slot inside an async method is gone
    /// from the caller's flow as soon as the caller continues after the await, so the scope is placed on the
    /// slot synchronously, before the lookup starts, and the lookup only binds the caller to it. A scope not
    /// bound yet, and one whose lookup found nobody or failed, is never the current caller.
    /// </remarks>
    /// <param name="resolve">Finds the administrator to act as, or null when none may act.</param>
    /// <returns>The scope to dispose, or null when no administrator was found.</returns>
    public Task<IDisposable?> ActAsync(Func<Task<AdministratorCaller?>> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);

        var scope = new Restoration(_current, _current.Value);
        _current.Value = scope;

        return BindAsync(scope, resolve);
    }

    /// <summary>Binds the found administrator to a scope already placed on the slot, or ends the scope.</summary>
    /// <param name="scope">The scope placed by <see cref="ActAsync"/>.</param>
    /// <param name="resolve">Finds the administrator to act as.</param>
    /// <returns>The scope, or null when no administrator was found.</returns>
    private static async Task<IDisposable?> BindAsync(Restoration scope, Func<Task<AdministratorCaller?>> resolve)
    {
        AdministratorCaller? administrator;

        try
        {
            administrator = await resolve();
        }
        catch
        {
            scope.Dispose();
            throw;
        }

        if (administrator is null)
        {
            scope.Dispose();

            return null;
        }

        scope.Bind(administrator);

        return scope;
    }

    /// <summary>One acting-as scope: the caller, whether it is still open, and the scope to put back.</summary>
    private sealed class Restoration : IDisposable
    {
        /// <summary>The slot to restore.</summary>
        private readonly AsyncLocal<Restoration?> _slot;

        /// <summary>Whether the scope has not been disposed; read by flows that copied it.</summary>
        private volatile bool _open = true;

        /// <summary>Who the job acts as inside this scope; null until bound.</summary>
        private volatile Caller? _caller;

        /// <summary>The scope to put back.</summary>
        public Restoration? Previous { get; }

        /// <summary>Who the job acts as inside this scope; null until bound.</summary>
        public Caller? Caller
        {
            get { return _caller; }
        }

        /// <summary>Whether the scope is bound to a caller and has not been disposed.</summary>
        public bool Active
        {
            get { return _open && _caller is not null; }
        }

        /// <summary>Creates the scope.</summary>
        /// <param name="slot">The slot to restore.</param>
        /// <param name="previous">The scope to put back.</param>
        public Restoration(AsyncLocal<Restoration?> slot, Restoration? previous)
        {
            _slot = slot;
            Previous = previous;
        }

        /// <summary>Makes the scope act as <paramref name="caller"/>, unless it has already ended.</summary>
        /// <param name="caller">Who the job acts as.</param>
        public void Bind(Caller caller)
        {
            _caller = caller;
        }

        /// <summary>Ends the scope for every flow that copied it, and puts the previous scope back on this one.</summary>
        public void Dispose()
        {
            _open = false;
            _slot.Value = Previous;
        }
    }
}
