using Maran.Modules.ServerMigration.Models;
using Maran.Modules.ServerMigration.Services;

namespace Maran.Modules.ServerMigration.Tests.Services;

/// <summary>What this process knows about the transfers it runs, and how a pause reaches one (issue #197, phase 2).</summary>
public sealed class MigrationRunRegistryTests
{
    /// <summary>A registered worker is running until released.</summary>
    [Fact]
    public void A_worker_is_running_between_register_and_release()
    {
        var registry = new MigrationRunRegistry();
        var account = Guid.NewGuid();

        Assert.False(registry.IsRunning(account));
        registry.Register(account, CancellationToken.None);
        Assert.True(registry.IsRunning(account));
        registry.Release(account);
        Assert.False(registry.IsRunning(account));
    }

    /// <summary>Stopping cancels the worker's token and records why; an untouched worker has no reason.</summary>
    [Fact]
    public void Stopping_cancels_the_token_and_remembers_the_reason()
    {
        var registry = new MigrationRunRegistry();
        var account = Guid.NewGuid();
        var token = registry.Register(account, CancellationToken.None);
        Assert.Null(registry.ReasonOf(account));

        registry.Stop(account, MigrationStopReason.Pause);

        Assert.True(token.IsCancellationRequested);
        Assert.Equal(MigrationStopReason.Pause, registry.ReasonOf(account));
    }

    /// <summary>Stopping an account nobody runs does nothing and does not throw.</summary>
    [Fact]
    public void Stopping_an_unknown_account_is_harmless()
    {
        var registry = new MigrationRunRegistry();

        registry.Stop(Guid.NewGuid(), MigrationStopReason.Cancel);
    }

    /// <summary>The host's shutdown stops a worker without anyone asking for a cancel, so the engine ends it as a pause and keeps its partial state.</summary>
    [Fact]
    public void A_shutdown_stops_the_worker_and_is_read_as_a_pause()
    {
        var registry = new MigrationRunRegistry();
        var account = Guid.NewGuid();
        using var shutdown = new CancellationTokenSource();
        var token = registry.Register(account, shutdown.Token);

        shutdown.Cancel();

        Assert.True(token.IsCancellationRequested);
        Assert.Equal(MigrationStopReason.Pause, registry.ReasonOf(account));
    }

    /// <summary>The gate of a job admits one decision at a time and another job's gate is separate.</summary>
    [Fact]
    public async Task The_gate_of_a_job_serialises_decisions_about_that_job_only()
    {
        var registry = new MigrationRunRegistry();
        var job = Guid.NewGuid();
        using var first = await registry.EnterAsync(job, CancellationToken.None);

        var second = registry.EnterAsync(job, CancellationToken.None);
        using var other = await registry.EnterAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(second.IsCompleted);
        first.Dispose();
        using var admitted = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(admitted);
    }
}
