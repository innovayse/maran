using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FilesService;
using Maran.Modules.Files.Queries.ListTrashEntries;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;
using FileEntryKind = Maran.Modules.Files.Domain.Enums.FileEntryKind;

namespace Maran.Modules.Files.Tests.Queries.ListTrashEntries;

/// <summary>
/// Holds <see cref="ListTrashEntriesQueryHandler"/> to listing only the caller's own account's trash, newest
/// deletion first by default, sorted and searched on the entries the agent reports, with an expiry worked out
/// from the retention setting.
/// </summary>
public sealed class ListTrashEntriesQueryHandlerTests
{
    /// <summary>When the oldest seeded entry was deleted.</summary>
    private static readonly DateTimeOffset First = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Builds the handler over a world's collaborators.</summary>
    /// <param name="world">The world.</param>
    /// <returns>The handler.</returns>
    private static ListTrashEntriesQueryHandler HandlerOf(FilesHandlerWorld world)
    {
        return new ListTrashEntriesQueryHandler(
            world.Gate, world.Client, world.Names, world.Monitor, NullLogger<ListTrashEntriesQueryHandler>.Instance);
    }

    /// <summary>Builds a trash listing query.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="search">The search text, or <see langword="null"/> for none.</param>
    /// <param name="sort">The sort key text, or <see langword="null"/> for the default.</param>
    /// <param name="page">The one-based page.</param>
    /// <param name="pageSize">The page size.</param>
    /// <returns>The query.</returns>
    private static ListTrashEntriesQuery QueryOf(
        Guid accountId,
        string? search = null,
        string? sort = null,
        int page = 1,
        int pageSize = 25)
    {
        return new ListTrashEntriesQuery(
            accountId,
            page,
            pageSize,
            search is null ? SearchTerm.None : SearchTerm.Of(search),
            sort is null ? SortRequest.Default : SortRequest.Parse(sort),
            FilesHandlerWorld.Address,
            FilesHandlerWorld.UserAgent);
    }

    /// <summary>Seeds the fake's trash with one entry.</summary>
    /// <param name="world">The world.</param>
    /// <param name="id">The trash id.</param>
    /// <param name="originalPath">Where the entry lived.</param>
    /// <param name="daysAfterFirst">How many days after <see cref="First"/> it was deleted.</param>
    /// <param name="size">Its size in bytes.</param>
    /// <param name="directory">Whether it was a folder.</param>
    private static void Trash(
        FilesHandlerWorld world,
        string id,
        string originalPath,
        int daysAfterFirst,
        ulong size,
        bool directory = false)
    {
        world.Client.TrashAnswer.Add(new TrashedEntryDto(id, originalPath, First.AddDays(daysAfterFirst), directory, size));
    }

    /// <summary>The newest deletion comes first, and every entry expires retention-days after it was deleted.</summary>
    [Fact]
    public async Task The_newest_deletion_comes_first_and_each_entry_expires_after_the_retention_days()
    {
        var world = FilesHandlerWorld.ForCustomer();
        Trash(world, "1-0000000000000001", "old.txt", 0, 10);
        Trash(world, "2-0000000000000002", "mid.txt", 1, 10);
        Trash(world, "3-0000000000000003", "new.txt", 2, 10);

        var result = await HandlerOf(world).HandleAsync(QueryOf(world.Active.Id), default);

        Assert.Equal(["new.txt", "mid.txt", "old.txt"], result.Value.Items.Select(item =>
        {
            return item.Name;
        }));
        var newest = result.Value.Items[0];
        Assert.Equal(First.AddDays(2), newest.DeletedAt);
        Assert.Equal(First.AddDays(2 + world.Limits.TrashRetentionDays), newest.ExpiresAt);
        var call = Assert.Single(world.Client.Calls);
        Assert.Equal(nameof(IAgentFilesClient.ListTrashAsync), call.Operation);
        Assert.Equal("alpha", call.Account);
    }

    /// <summary>A size sort orders the listing by size, largest first when descending.</summary>
    [Fact]
    public async Task A_descending_size_sort_puts_the_largest_entry_first()
    {
        var world = FilesHandlerWorld.ForCustomer();
        Trash(world, "1-0000000000000001", "a.txt", 0, 10);
        Trash(world, "2-0000000000000002", "b.txt", 1, 300);
        Trash(world, "3-0000000000000003", "c.txt", 2, 20);

        var result = await HandlerOf(world).HandleAsync(
            QueryOf(world.Active.Id, sort: "-" + TrashEntrySortFields.Size), default);

        Assert.Equal(["b.txt", "c.txt", "a.txt"], result.Value.Items.Select(item =>
        {
            return item.Name;
        }));
    }

    /// <summary>A search matches the name or the original path, ignoring case.</summary>
    [Fact]
    public async Task A_search_matches_the_name_or_the_original_path_ignoring_case()
    {
        var world = FilesHandlerWorld.ForCustomer();
        Trash(world, "1-0000000000000001", "docs/Report.pdf", 0, 10);
        Trash(world, "2-0000000000000002", "images/cat.png", 1, 10);
        Trash(world, "3-0000000000000003", "other.txt", 2, 10);

        var byName = await HandlerOf(world).HandleAsync(QueryOf(world.Active.Id, search: "report"), default);
        var byPath = await HandlerOf(world).HandleAsync(QueryOf(world.Active.Id, search: "IMAGES"), default);

        Assert.Equal(["Report.pdf"], byName.Value.Items.Select(item =>
        {
            return item.Name;
        }));
        Assert.Equal("docs/Report.pdf", byName.Value.Items[0].OriginalPath);
        Assert.Equal(["cat.png"], byPath.Value.Items.Select(item =>
        {
            return item.Name;
        }));
    }

    /// <summary>The total counts every match while a page holds only its window.</summary>
    [Fact]
    public async Task A_page_holds_its_window_and_the_total_counts_every_entry()
    {
        var world = FilesHandlerWorld.ForCustomer();
        for (var index = 0; index < 12; index++)
        {
            Trash(world, $"{index + 1}-{index:D16}", $"f{index}.txt", index, 1);
        }

        var result = await HandlerOf(world).HandleAsync(QueryOf(world.Active.Id, page: 2, pageSize: 10), default);

        Assert.Equal(12, result.Value.Total);
        Assert.Equal(["f1.txt", "f0.txt"], result.Value.Items.Select(item =>
        {
            return item.Name;
        }));
    }

    /// <summary>A folder is reported as a folder, with its localized kind name.</summary>
    [Fact]
    public async Task A_folder_is_reported_as_a_folder_with_its_kind_name()
    {
        var world = FilesHandlerWorld.ForCustomer();
        Trash(world, "1-0000000000000001", "site/assets", 0, 4096, directory: true);

        var result = await HandlerOf(world).HandleAsync(QueryOf(world.Active.Id), default);

        var item = Assert.Single(result.Value.Items);
        Assert.Equal("assets", item.Name);
        Assert.Equal(FileEntryKind.Directory, item.Kind);
        Assert.Equal("FileEntryKindDirectory", item.KindName);
    }

    /// <summary>An agent failure is a generic failure that carries nothing of the agent's own text.</summary>
    [Fact]
    public async Task An_agent_failure_is_a_generic_failure()
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.FailOn(
            nameof(IAgentFilesClient.ListTrashAsync), string.Empty, Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await HandlerOf(world).HandleAsync(QueryOf(world.Active.Id), default);

        Assert.Equal("FilesAgentFailed", result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
    }

    /// <summary>Another tenant's account is not found, the agent is never asked, and the probe is journalled.</summary>
    [Fact]
    public async Task Another_tenants_account_is_not_found_and_the_probe_is_journalled()
    {
        var world = FilesHandlerWorld.ForCustomer();

        var result = await HandlerOf(world).HandleAsync(QueryOf(world.Stranger), default);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(world.Client.Calls);
        var entry = Assert.Single(world.Writer.Entries);
        Assert.Equal(FileAuditActions.FileAccessRefused, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A suspended account refuses a customer and admits an administrator.</summary>
    [Fact]
    public async Task A_suspended_account_refuses_a_customer_and_admits_an_administrator()
    {
        var customer = FilesHandlerWorld.ForCustomer();
        var administrator = FilesHandlerWorld.ForAdministrator();

        var refused = await HandlerOf(customer).HandleAsync(QueryOf(customer.Suspended.Id), default);
        var admitted = await HandlerOf(administrator).HandleAsync(QueryOf(administrator.Suspended.Id), default);

        Assert.Equal("FilesAccountSuspended", refused.Error!.Code);
        Assert.Empty(customer.Client.Calls);
        Assert.True(admitted.IsSuccess);
    }
}
