using Maran.Host.Modules;
using Maran.Host.Tests.Fixtures;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.Http;

namespace Maran.Host.Tests.Modules;

/// <summary>
/// Behavioral contract of <see cref="ModuleLoaderAuditJournal"/>: it records an administrator's step under
/// their name with the request's origin, records unattended work as the panel, and never writes a part of a
/// subject that a package's author could have shaped.
/// </summary>
public sealed class ModuleLoaderAuditJournalTests
{
    /// <summary>An administrator's step is recorded under their name, with the address and agent of the request.</summary>
    [Fact]
    public async Task An_administrators_step_is_recorded_under_their_name_with_the_requests_origin()
    {
        var writer = new CapturingAuditWriter();
        var administrator = new AdministratorCaller(Guid.NewGuid(), "olive");
        var request = new DefaultHttpContext();
        request.Request.Headers.UserAgent = "test-agent";
        var journal = JournalFor(writer, administrator, request);

        await journal.RecordAsync(MarketplaceAuditActions.ModuleInstalled, "hello", "1.2.0", null, true, CancellationToken.None);

        var entry = Assert.Single(writer.Entries);
        Assert.Equal(administrator.UserId, entry.ActorUserId);
        Assert.Equal("olive", entry.ActorUsername);
        Assert.Equal(MarketplaceAuditActions.ModuleInstalled, entry.Action);
        Assert.Equal("module=hello;version=1.2.0", entry.Subject);
        Assert.Equal("test-agent", entry.UserAgent);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Work nobody asked for is recorded as the panel, with no origin.</summary>
    [Fact]
    public async Task Work_nobody_asked_for_is_recorded_as_the_panel_with_no_origin()
    {
        var writer = new CapturingAuditWriter();
        var journal = JournalFor(writer, AnonymousCaller.Instance, new DefaultHttpContext());

        await journal.RecordAsync(
            MarketplaceAuditActions.ModuleRefused,
            "hello",
            "1.2.0",
            Error.Of("ModuleIdTaken", ErrorType.Conflict),
            false,
            CancellationToken.None);

        var entry = Assert.Single(writer.Entries);
        Assert.Null(entry.ActorUserId);
        Assert.Equal("maran-marketplace", entry.ActorUsername);
        Assert.Equal(string.Empty, entry.IpAddress);
        Assert.Equal(string.Empty, entry.UserAgent);
        Assert.Equal("module=hello;version=1.2.0;reason=ModuleIdTaken", entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A refusal about a package with a hostile manifest writes none of the hostile parts.</summary>
    [Fact]
    public async Task A_refusal_about_a_hostile_manifest_writes_none_of_the_hostile_parts()
    {
        var writer = new CapturingAuditWriter();
        var journal = JournalFor(writer, AnonymousCaller.Instance, new DefaultHttpContext());

        await journal.RecordAsync(
            MarketplaceAuditActions.ModuleRefused,
            "x;reason=forged\nline",
            "not a version; module=sites",
            Error.Of("../../etc/passwd", ErrorType.Validation),
            false,
            CancellationToken.None);

        Assert.Equal(ModuleLoaderAuditJournal.UnreadableSubject, Assert.Single(writer.Entries).Subject);
    }

    /// <summary>A missing manifest reads as unreadable, not as an empty subject.</summary>
    [Fact]
    public async Task A_missing_manifest_reads_as_unreadable()
    {
        var writer = new CapturingAuditWriter();
        var journal = JournalFor(writer, AnonymousCaller.Instance, new DefaultHttpContext());

        await journal.RecordAsync(MarketplaceAuditActions.ModuleRefused, null, null, null, false, CancellationToken.None);

        Assert.Equal(ModuleLoaderAuditJournal.UnreadableSubject, Assert.Single(writer.Entries).Subject);
    }

    /// <summary>An action name must be given.</summary>
    [Fact]
    public async Task An_action_must_be_given()
    {
        var journal = JournalFor(new CapturingAuditWriter(), AnonymousCaller.Instance, new DefaultHttpContext());

        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            return journal.RecordAsync(" ", "hello", null, null, true, CancellationToken.None);
        });
    }

    /// <summary>Builds the journal under test.</summary>
    /// <param name="writer">Where entries are captured.</param>
    /// <param name="caller">Who the request is made by.</param>
    /// <param name="request">The request in progress.</param>
    /// <returns>The journal.</returns>
    private static ModuleLoaderAuditJournal JournalFor(CapturingAuditWriter writer, Caller caller, HttpContext request)
    {
        return new ModuleLoaderAuditJournal(
            writer,
            new FixedCurrentUser(caller),
            new HttpContextAccessor { HttpContext = request });
    }

    /// <summary>An <see cref="IAuditWriter"/> that keeps what it is given.</summary>
    private sealed class CapturingAuditWriter : IAuditWriter
    {
        /// <summary>The entries written, in order.</summary>
        public List<AuditEntry> Entries { get; } = [];

        /// <inheritdoc />
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
