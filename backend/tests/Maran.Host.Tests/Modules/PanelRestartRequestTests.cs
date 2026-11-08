using Maran.Host.Modules;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Maran.Host.Tests.Modules;

/// <summary>
/// Behavioral contract of <see cref="PanelRestartRequest"/>: a restart is recorded before it is
/// requested, asks the host to stop, makes the process exit non-zero, and does nothing at all when the
/// record cannot be written.
/// </summary>
public sealed class PanelRestartRequestTests
{
    /// <summary>A request is audited, asks the host to stop and marks the exit as requested.</summary>
    [Fact]
    public async Task A_request_is_audited_asks_the_host_to_stop_and_marks_the_exit_as_requested()
    {
        var audit = new RecordingAudit();
        var lifetime = new CountingLifetime();
        var request = RequestWith(audit, lifetime);
        Assert.False(request.Requested);

        await request.RequestAsync("hello", CancellationToken.None);

        var recorded = Assert.Single(audit.Actions);
        Assert.Equal((MarketplaceAuditActions.ModuleRestartRequested, "hello"), recorded);
        Assert.Equal(1, lifetime.Stops);
        Assert.True(request.Requested);
    }

    /// <summary>When the audit write fails, nothing is requested and the panel keeps running.</summary>
    [Fact]
    public async Task When_the_audit_write_fails_nothing_is_requested()
    {
        var lifetime = new CountingLifetime();
        var request = RequestWith(new RecordingAudit { Fail = true }, lifetime);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            return request.RequestAsync("hello", CancellationToken.None);
        });

        Assert.Equal(0, lifetime.Stops);
        Assert.False(request.Requested);
    }

    /// <summary>A module id must be given.</summary>
    [Fact]
    public async Task A_module_id_must_be_given()
    {
        var request = RequestWith(new RecordingAudit(), new CountingLifetime());

        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            return request.RequestAsync(" ", CancellationToken.None);
        });
    }

    /// <summary>The exit status of a requested restart is non-zero, because only a failure exit is restarted.</summary>
    [Fact]
    public void The_exit_status_of_a_requested_restart_is_non_zero()
    {
        Assert.NotEqual(0, PanelRestartRequest.ExitStatus);
    }

    /// <summary>Builds the request handler with stand-ins for the journal and the host.</summary>
    /// <param name="audit">The journal stand-in.</param>
    /// <param name="lifetime">The host stand-in.</param>
    /// <returns>The handler.</returns>
    private static PanelRestartRequest RequestWith(RecordingAudit audit, CountingLifetime lifetime)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IModuleLoaderAudit>(audit);

        return new PanelRestartRequest(
            lifetime,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
    }

    /// <summary>An <see cref="IModuleLoaderAudit"/> that keeps what it is given, or refuses it.</summary>
    private sealed class RecordingAudit : IModuleLoaderAudit
    {
        /// <summary>The action and module id of each record, in order.</summary>
        public List<(string Action, string? ModuleId)> Actions { get; } = [];

        /// <summary>Whether the write fails.</summary>
        public bool Fail { get; init; }

        /// <inheritdoc />
        public Task RecordAsync(
            string action,
            string? moduleId,
            string? version,
            Error? reason,
            bool succeeded,
            CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("The journal is unavailable.");
            }

            Actions.Add((action, moduleId));
            return Task.CompletedTask;
        }
    }

    /// <summary>An <see cref="IHostApplicationLifetime"/> that counts the stops it is asked for.</summary>
    private sealed class CountingLifetime : IHostApplicationLifetime
    {
        /// <summary>How many times the host was asked to stop.</summary>
        public int Stops { get; private set; }

        /// <inheritdoc />
        public CancellationToken ApplicationStarted
        {
            get { return CancellationToken.None; }
        }

        /// <inheritdoc />
        public CancellationToken ApplicationStopping
        {
            get { return CancellationToken.None; }
        }

        /// <inheritdoc />
        public CancellationToken ApplicationStopped
        {
            get { return CancellationToken.None; }
        }

        /// <inheritdoc />
        public void StopApplication()
        {
            Stops++;
        }
    }
}
