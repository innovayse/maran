using Maran.Agent.Client.Services.FilesService;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.FilesService;

/// <summary>Mapping contract of the browse side of AgentFilesClient: list and search (proto oneof to Result).</summary>
public sealed class AgentFilesClientBrowseTests
{
    /// <summary>A listing sends every paging, ordering and visibility choice to the agent unchanged.</summary>
    [Fact]
    public async Task A_listing_sends_every_paging_ordering_and_visibility_choice_unchanged()
    {
        var stub = new StubFilesService
        {
            ListResponse = new ListDirectoryResponse { Ok = new ListDirectoryOk() },
        };

        await ClientFor(stub).ListDirectoryAsync(
            "acct", "sites/shop", 50, 25, true, DirectorySortOrder.Modified, true, CancellationToken.None);

        var request = Assert.IsType<ListDirectoryRequest>(stub.LastListRequest);
        Assert.Equal("acct", request.AccountUsername);
        Assert.Equal("sites/shop", request.Path);
        Assert.Equal(50u, request.Offset);
        Assert.Equal(25u, request.Limit);
        Assert.True(request.IncludeHidden);
        Assert.Equal(DirectorySort.Modified, request.Sort);
        Assert.True(request.Descending);
    }

    /// <summary>Each panel sort order reaches the agent as its own wire order.</summary>
    [Theory]
    [InlineData(DirectorySortOrder.Name, DirectorySort.Name)]
    [InlineData(DirectorySortOrder.Size, DirectorySort.Size)]
    [InlineData(DirectorySortOrder.Modified, DirectorySort.Modified)]
    public async Task Each_panel_sort_order_reaches_the_agent_as_its_own_wire_order(
        DirectorySortOrder asked,
        DirectorySort sent)
    {
        var stub = new StubFilesService
        {
            ListResponse = new ListDirectoryResponse { Ok = new ListDirectoryOk() },
        };

        await ClientFor(stub).ListDirectoryAsync("acct", "", 0, 0, false, asked, false, CancellationToken.None);

        Assert.Equal(sent, stub.LastListRequest!.Sort);
    }

    /// <summary>An ok listing maps every entry field and the counts of the page.</summary>
    [Fact]
    public async Task An_ok_listing_maps_every_entry_field_and_the_counts_of_the_page()
    {
        var ok = new ListDirectoryOk { TotalEntries = 9, SkippedEntries = 2 };
        ok.Entries.Add(new DirectoryEntry
        {
            Name = "index.html",
            SizeBytes = 120,
            Mode = 0b110_100_100,
            ModifiedAtUnix = 1_700_000_000,
            Uid = 1001,
            Gid = 1002,
            LinkCount = 1,
        });
        var stub = new StubFilesService { ListResponse = new ListDirectoryResponse { Ok = ok } };

        var result = await ClientFor(stub).ListDirectoryAsync(
            "acct", "", 0, 0, false, DirectorySortOrder.Name, false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(9u, result.Value.TotalEntries);
        Assert.Equal(2u, result.Value.SkippedEntries);
        var entry = Assert.Single(result.Value.Entries);
        Assert.Equal("index.html", entry.Name);
        Assert.Equal(FileEntryKind.File, entry.Kind);
        Assert.Equal(120ul, entry.SizeBytes);
        Assert.Equal(0b110_100_100u, entry.Mode);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), entry.ModifiedAt);
        Assert.Equal(1001u, entry.OwnerId);
        Assert.Equal(1002u, entry.GroupId);
        Assert.Equal(1ul, entry.LinkCount);
    }

    /// <summary>A link to a directory is a link, a real directory is a directory, and anything else is a file.</summary>
    [Fact]
    public async Task A_link_to_a_directory_is_a_link_and_not_a_directory()
    {
        var ok = new ListDirectoryOk();
        ok.Entries.Add(new DirectoryEntry { Name = "real", IsDirectory = true });
        ok.Entries.Add(new DirectoryEntry { Name = "link", IsSymlink = true });
        ok.Entries.Add(new DirectoryEntry { Name = "pipe" });
        var stub = new StubFilesService { ListResponse = new ListDirectoryResponse { Ok = ok } };

        var result = await ClientFor(stub).ListDirectoryAsync(
            "acct", "", 0, 0, false, DirectorySortOrder.Name, false, CancellationToken.None);

        Assert.Equal(
            [FileEntryKind.Directory, FileEntryKind.Symlink, FileEntryKind.File],
            result.Value.Entries.Select(entry => { return entry.Kind; }));
    }

    /// <summary>A modification time outside what a date can hold is clamped, not thrown on.</summary>
    [Theory]
    [InlineData(long.MaxValue, 253_402_300_799)]
    [InlineData(long.MinValue, -62_135_596_800)]
    public async Task A_modification_time_outside_what_a_date_can_hold_is_clamped(long wire, long expectedSeconds)
    {
        var ok = new ListDirectoryOk();
        ok.Entries.Add(new DirectoryEntry { Name = "odd", ModifiedAtUnix = wire });
        var stub = new StubFilesService { ListResponse = new ListDirectoryResponse { Ok = ok } };

        var result = await ClientFor(stub).ListDirectoryAsync(
            "acct", "", 0, 0, false, DirectorySortOrder.Name, false, CancellationToken.None);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(expectedSeconds), result.Value.Entries[0].ModifiedAt);
    }

    /// <summary>A refused listing maps to a failed result carrying only a code, and a too-large directory has its own.</summary>
    [Theory]
    [InlineData(ErrorCode.TooLarge, "AgentTooLarge", ErrorType.Validation)]
    [InlineData(ErrorCode.ValidationFailed, "AgentValidationFailed", ErrorType.Validation)]
    [InlineData(ErrorCode.AgentSaturated, "AgentSaturated", ErrorType.Unavailable)]
    public async Task A_refused_listing_maps_to_a_failed_result_carrying_only_a_code(
        ErrorCode wire,
        string code,
        ErrorType type)
    {
        var stub = new StubFilesService
        {
            ListResponse = new ListDirectoryResponse
            {
                Error = new AgentError { Code = wire, Message = "/home/acct/private is a symlink" },
            },
        };

        var result = await ClientFor(stub).ListDirectoryAsync(
            "acct", "private", 0, 0, false, DirectorySortOrder.Name, false, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(type, result.Error.Type);
    }

    /// <summary>A listing response with neither branch set is an invalid response, not an empty directory.</summary>
    [Fact]
    public async Task A_listing_response_with_neither_branch_is_an_invalid_response()
    {
        var result = await ClientFor(new StubFilesService()).ListDirectoryAsync(
            "acct", "", 0, 0, false, DirectorySortOrder.Name, false, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>A search sends the account, the start, the term, the bounds and the visibility to the agent.</summary>
    [Fact]
    public async Task A_search_sends_the_start_the_term_the_bounds_and_the_visibility()
    {
        var stub = new StubFilesService
        {
            SearchResponse = new SearchEntriesResponse { Ok = new SearchEntriesOk() },
        };

        await ClientFor(stub).SearchEntriesAsync("acct", "sites", "logo", 4, 80, true, CancellationToken.None);

        var request = Assert.IsType<SearchEntriesRequest>(stub.LastSearchRequest);
        Assert.Equal("acct", request.AccountUsername);
        Assert.Equal("sites", request.Path);
        Assert.Equal("logo", request.Query);
        Assert.Equal(4u, request.MaxDepth);
        Assert.Equal(80u, request.MaxResults);
        Assert.True(request.IncludeHidden);
    }

    /// <summary>An ok search maps each match with its path and entry, and a bound that stopped it is a success.</summary>
    [Fact]
    public async Task An_ok_search_maps_each_match_and_a_truncated_search_is_still_a_success()
    {
        var ok = new SearchEntriesOk { Truncated = true, SkippedEntries = 1 };
        ok.Matches.Add(new SearchMatch
        {
            Path = "sites/shop/logo.png",
            Entry = new DirectoryEntry { Name = "logo.png", SizeBytes = 2048 },
        });
        var stub = new StubFilesService { SearchResponse = new SearchEntriesResponse { Ok = ok } };

        var result = await ClientFor(stub).SearchEntriesAsync("acct", "", "logo", 0, 0, false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Truncated);
        Assert.Equal(1u, result.Value.SkippedEntries);
        var match = Assert.Single(result.Value.Matches);
        Assert.Equal("sites/shop/logo.png", match.Path);
        Assert.Equal("logo.png", match.Entry.Name);
        Assert.Equal(2048ul, match.Entry.SizeBytes);
    }

    /// <summary>A match with no entry is an invalid response and not an empty file with no name.</summary>
    [Fact]
    public async Task A_match_with_no_entry_is_an_invalid_response()
    {
        var ok = new SearchEntriesOk();
        ok.Matches.Add(new SearchMatch { Path = "a/b" });
        var stub = new StubFilesService { SearchResponse = new SearchEntriesResponse { Ok = ok } };

        var result = await ClientFor(stub).SearchEntriesAsync("acct", "", "b", 0, 0, false, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>A refused search maps to a failed result carrying only a code.</summary>
    [Fact]
    public async Task A_refused_search_maps_to_a_failed_result_carrying_only_a_code()
    {
        var stub = new StubFilesService
        {
            SearchResponse = new SearchEntriesResponse
            {
                Error = new AgentError { Code = ErrorCode.InvalidInput, Message = "query holds a slash" },
            },
        };

        var result = await ClientFor(stub).SearchEntriesAsync("acct", "", "a/b", 0, 0, false, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidInput", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    /// <summary>Builds a client over the stub with a logger that discards.</summary>
    /// <param name="stub">The transport stand-in.</param>
    /// <returns>The client under test.</returns>
    private static AgentFilesClient ClientFor(StubFilesService stub)
    {
        return new AgentFilesClient(stub, NullLogger<AgentFilesClient>.Instance);
    }
}
