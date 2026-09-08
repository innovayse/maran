using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Options;
using Maran.Modules.Tasks.Persistence;
using Maran.Modules.Tasks.Resources;
using Maran.Modules.Tasks.Services;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Tasks.Tests.TestSupport;

/// <summary>
/// Builds isolated <see cref="TasksDbContext"/> instances seen as a given principal, plus the rows
/// and settings to go with them. Each context gets its own uniquely-named in-memory database unless
/// a caller passes a shared name, which is what a visibility test needs: two contexts, two
/// principals, ONE database, so the only thing separating the rows is the query filter under test.
/// </summary>
public static class TasksTestContext
{
    /// <summary>The instant every seeded task starts at, so nothing in these tests reads a clock.</summary>
    public static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Creates a context over a database, seen as <paramref name="currentUser"/>.</summary>
    /// <param name="currentUser">The principal whose visibility the context is bound to.</param>
    /// <param name="databaseName">The in-memory database to open; a fresh one when omitted.</param>
    /// <param name="saveFailure">When given, the exception saves throw once the allowance is spent.</param>
    /// <param name="savesBeforeFailure">How many saves succeed before <paramref name="saveFailure"/> starts.</param>
    /// <returns>The context.</returns>
    public static TasksDbContext Create(
        ICurrentUser currentUser,
        string? databaseName = null,
        Exception? saveFailure = null,
        int savesBeforeFailure = 0)
    {
        var builder = new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString());

        if (saveFailure is not null)
        {
            builder.AddInterceptors(new FailingSaveInterceptor(saveFailure, savesBeforeFailure));
        }

        return new TasksDbContext(builder.Options, currentUser);
    }

    /// <summary>Builds a running task row.</summary>
    /// <param name="kind">What kind of operation it records.</param>
    /// <param name="subject">What it acts on.</param>
    /// <param name="startedAt">When it started; the fixture's own instant when omitted.</param>
    /// <returns>The row, at revision zero.</returns>
    public static PanelTask Row(
        string kind = "CertificateIssue",
        string subject = "example.com",
        DateTimeOffset? startedAt = null)
    {
        return new PanelTask(Guid.NewGuid(), kind, subject, correlationId: null, startedAt ?? Now);
    }

    /// <summary>Builds stream settings with a poll interval short enough for a test to wait on.</summary>
    /// <param name="pollIntervalMilliseconds">How often the reader re-reads its row.</param>
    /// <param name="heartbeatSeconds">How long the writer may be silent before a keep-alive.</param>
    /// <returns>The settings, as the options monitor would supply them.</returns>
    public static IOptions<TaskStreamOptions> StreamOptions(
        int pollIntervalMilliseconds = 100,
        int heartbeatSeconds = 600)
    {
        return new OptionsWrapper<TaskStreamOptions>(new TaskStreamOptions
        {
            PollIntervalMilliseconds = pollIntervalMilliseconds,
            HeartbeatSeconds = heartbeatSeconds,
        });
    }

    /// <summary>
    /// Builds the kind-name resolver over the module's REAL <c>DisplayNames</c> resources.
    /// </summary>
    /// <returns>The resolver, reading the shipped resx rather than a stub.</returns>
    /// <remarks>
    /// A stub localizer would prove only that the handler calls something; the thing worth checking
    /// is that a shipped <c>TaskKinds</c> constant actually has an entry under this key scheme, and
    /// that is only observable against the real resource files (rules/testing.md "A check must be
    /// able to observe what it reports on").
    /// </remarks>
    public static TaskKindDisplayNames KindNames()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);

        return new TaskKindDisplayNames(
            new StringLocalizer<DisplayNames>(factory));
    }
}
