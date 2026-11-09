using Maran.Modules.Files.Models;
using Maran.Modules.Files.Options;
using Maran.Modules.Files.Services;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Files.Tests.Services;

/// <summary>Holds <see cref="FilesBatchRunner"/> to capping first, continuing past a failure and auditing each item.</summary>
public sealed class FilesBatchRunnerTests
{
    /// <summary>Where a call came from, for every entry.</summary>
    private static readonly FilesCallOrigin Origin = new("203.0.113.9", "tests");

    /// <summary>The account every batch is on.</summary>
    private readonly AccountSnapshot _account = FilesTestAccounts.Account(Guid.NewGuid(), "alpha", AccountState.Active);

    /// <summary>What the journal wrote.</summary>
    private readonly RecordingAuditWriter _writer = new();

    /// <summary>Builds a runner with a batch ceiling.</summary>
    /// <param name="maxItems">The most items one batch may name.</param>
    /// <returns>The runner.</returns>
    private FilesBatchRunner Runner(int maxItems)
    {
        var journal = new FilesAuditJournal(
            _writer, new FakeCurrentUser(new AdministratorCaller(Guid.NewGuid(), "root")));
        return new FilesBatchRunner(
            journal,
            new StubErrorTextProvider(),
            new FixedOptionsMonitor<FilesOptions>(new FilesOptions { MaxBatchItems = maxItems }));
    }

    /// <summary>A failure of one item does not stop the others, and each item is audited on its own.</summary>
    [Fact]
    public async Task A_failed_item_does_not_stop_the_others_and_every_item_is_audited()
    {
        var calls = new List<string>();

        var result = await Runner(10).RunAsync(
            _account,
            FileAuditActions.FileTrashed,
            ["a", "b", "c"],
            Origin,
            (subject, _) =>
            {
                calls.Add(subject);
                return Task.FromResult(subject == "b"
                    ? Result.Fail(Error.Of("FilesEntryNotFound", ErrorType.NotFound))
                    : Result.Ok());
            },
            default);

        Assert.Equal(["a", "b", "c"], calls);
        var items = result.Value!.Items;
        Assert.Equal([true, false, true], items.Select(item =>
        {
            return item.Succeeded;
        }));
        Assert.Equal("FilesEntryNotFound", items[1].Code);
        Assert.Equal("text:FilesEntryNotFound", items[1].Message);
        Assert.Null(items[0].Code);
        Assert.Equal(["alpha:a", "alpha:b", "alpha:c"], _writer.Entries.Select(entry =>
        {
            return entry.Subject;
        }));
        Assert.Equal([true, false, true], _writer.Entries.Select(entry =>
        {
            return entry.Succeeded;
        }));
    }

    /// <summary>A batch over the ceiling is refused before the operation runs once.</summary>
    [Fact]
    public async Task A_batch_over_the_ceiling_is_refused_before_any_item_runs()
    {
        var calls = 0;

        var result = await Runner(2).RunAsync(
            _account,
            FileAuditActions.FileTrashed,
            ["a", "b", "c"],
            Origin,
            (_, _) =>
            {
                calls++;
                return Task.FromResult(Result.Ok());
            },
            default);

        Assert.Equal("FilesBatchTooLarge", result.Error!.Code);
        Assert.Equal(0, calls);
        Assert.False(Assert.Single(_writer.Entries).Succeeded);
    }

    /// <summary>An empty batch is refused.</summary>
    [Fact]
    public async Task An_empty_batch_is_refused()
    {
        var result = await Runner(2).RunAsync(
            _account, FileAuditActions.FileTrashed, [], Origin, (_, _) =>
            {
                return Task.FromResult(Result.Ok());
            }, default);

        Assert.Equal("FilesBatchEmpty", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }
}
