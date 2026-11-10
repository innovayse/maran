using Maran.Sdk.Interfaces;

namespace Maran.Modules.ServerMigration.Tests.TestSupport;

/// <summary>The job-caller opener with its answer set by the test: a scope for the initiator, or none.</summary>
public sealed class ScriptedJobCallerScopes : IJobCallerScopes
{
    /// <summary>Whether the initiator may still act; when false no scope is opened.</summary>
    public bool InitiatorMayAct { get; set; } = true;

    /// <summary>How many scopes were opened.</summary>
    public int Opened { get; private set; }

    /// <summary>How many opened scopes have been disposed.</summary>
    public int Closed { get; private set; }

    /// <inheritdoc />
    public Task<IDisposable?> BeginAsync(Guid initiatorUserId, CancellationToken cancellationToken)
    {
        if (!InitiatorMayAct)
        {
            return Task.FromResult<IDisposable?>(null);
        }

        Opened++;

        return Task.FromResult<IDisposable?>(new Scope(this));
    }

    /// <summary>One opened scope, counting its own disposal.</summary>
    private sealed class Scope : IDisposable
    {
        /// <summary>The opener that made it.</summary>
        private readonly ScriptedJobCallerScopes _owner;

        /// <summary>Creates the scope.</summary>
        /// <param name="owner">The opener that made it.</param>
        public Scope(ScriptedJobCallerScopes owner)
        {
            _owner = owner;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _owner.Closed++;
        }
    }
}
