using Maran.Host.Modules;
using Maran.Host.Tests.Modules.Fixtures;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.Sdk.Modules;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Host.Tests.Modules;

/// <summary>
/// Behavioral contract of <see cref="PackagedModuleRefusalRecorder"/>: every refusal of the start reaches
/// the journal as a failed entry, a clean start writes nothing, and a journal that cannot be written does
/// not stop the start.
/// </summary>
public sealed class PackagedModuleRefusalRecorderTests
{
    /// <summary>Every refusal of the start is recorded as a failed entry carrying its reason.</summary>
    [Fact]
    public async Task Every_refusal_of_the_start_is_recorded_as_a_failed_entry_carrying_its_reason()
    {
        var audit = new RecordingAudit();
        var recorder = RecorderFor(Refusing(), audit);

        await recorder.StartAsync(CancellationToken.None);

        var recorded = Assert.Single(audit.Records);
        Assert.Equal(MarketplaceAuditActions.ModuleRefused, recorded.Action);
        Assert.Equal("firewall", recorded.ModuleId);
        Assert.Equal("ModuleIdTaken", recorded.Reason);
        Assert.False(recorded.Succeeded);
    }

    /// <summary>A refusal that has no manifest is recorded with no module id, still as a failed entry with its reason.</summary>
    [Fact]
    public async Task A_refusal_with_no_manifest_is_recorded_without_a_module_id()
    {
        var audit = new RecordingAudit();
        var composition = new PackagedModuleComposition(
            ModuleRegistry.All, [], [new PackagedModuleRefusal(null, ModuleRefusals.ManifestUnreadable)]);

        await RecorderFor(composition, audit).StartAsync(CancellationToken.None);

        var recorded = Assert.Single(audit.Records);
        Assert.Equal(MarketplaceAuditActions.ModuleRefused, recorded.Action);
        Assert.Null(recorded.ModuleId);
        Assert.Equal("ModuleManifestUnreadable", recorded.Reason);
        Assert.False(recorded.Succeeded);
    }

    /// <summary>A start that refused nothing writes nothing.</summary>
    [Fact]
    public async Task A_start_that_refused_nothing_writes_nothing()
    {
        var audit = new RecordingAudit();
        var clean = new PackagedModuleComposition(ModuleRegistry.All, [], []);

        await RecorderFor(clean, audit).StartAsync(CancellationToken.None);

        Assert.Empty(audit.Records);
    }

    /// <summary>A journal that cannot be written does not stop the start.</summary>
    [Fact]
    public async Task A_journal_that_cannot_be_written_does_not_stop_the_start()
    {
        var audit = new RecordingAudit { Fail = true };
        var recorder = RecorderFor(Refusing(), audit);

        var thrown = await Record.ExceptionAsync(() => { return recorder.StartAsync(CancellationToken.None); });

        Assert.Null(thrown);
        Assert.Empty(audit.Records);
    }

    /// <summary>A composition in which one packaged module was refused.</summary>
    /// <returns>The composition.</returns>
    private static PackagedModuleComposition Refusing()
    {
        var firewall = ModuleRegistry.All.Single(module => { return module.Manifest.Id == "firewall"; });

        return PackagedModuleComposer.Compose(ModuleRegistry.All, [firewall], new FixedPanelVersion("99.0.0"));
    }

    /// <summary>Builds the recorder under test.</summary>
    /// <param name="composition">What the start composed and refused.</param>
    /// <param name="audit">The journal stand-in.</param>
    /// <returns>The recorder.</returns>
    private static PackagedModuleRefusalRecorder RecorderFor(PackagedModuleComposition composition, RecordingAudit audit)
    {
        var services = new ServiceCollection();
        services.AddScoped<IModuleLoaderAudit>(_ => { return audit; });

        return new PackagedModuleRefusalRecorder(
            composition,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PackagedModuleRefusalRecorder>.Instance);
    }

    /// <summary>An <see cref="IModuleLoaderAudit"/> that keeps what it is given, or refuses it.</summary>
    private sealed class RecordingAudit : IModuleLoaderAudit
    {
        /// <summary>What each record carried, in order.</summary>
        public List<(string Action, string? ModuleId, string? Reason, bool Succeeded)> Records { get; } = [];

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

            Records.Add((action, moduleId, reason?.Code, succeeded));
            return Task.CompletedTask;
        }
    }
}
