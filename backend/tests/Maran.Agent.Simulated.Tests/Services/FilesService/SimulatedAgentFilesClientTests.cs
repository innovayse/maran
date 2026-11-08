using System.Text;
using Maran.Agent.Client.Services.FilesService;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Services.FilesService;
using Maran.Agent.Simulated.State;

namespace Maran.Agent.Simulated.Tests.Services.FilesService;

/// <summary>
/// The simulated agent's file operations are contained, bounded, and never read a file as anything but data.
/// </summary>
public sealed class SimulatedAgentFilesClientTests
{
    /// <summary>Writing a file reports the bytes of its UTF-8 content, not its characters.</summary>
    [Fact]
    public async Task Writing_a_file_reports_the_bytes_of_its_utf8_content()
    {
        var (client, ledger) = NewClient();

        var armenian = "\u0540\u0561\u0575";

        var written = await client.WriteFileAsync(
            "alice",
            "notes/hi.txt",
            armenian,
            0b110_100_100,
            CancellationToken.None);

        Assert.Equal(6UL, written.Value);
        Assert.Equal(armenian, ledger.Files.Find("alice", "notes/hi.txt")!.Content);
    }

    /// <summary>A streamed write stores the body as the file's content and reports its bytes.</summary>
    [Fact]
    public async Task A_streamed_write_stores_the_body_as_the_files_content_and_reports_its_bytes()
    {
        var (client, ledger) = NewClient();
        using var body = new MemoryStream(Encoding.UTF8.GetBytes("hello"));

        var written = await client.WriteFileStreamAsync(
            "alice", "notes/up.txt", 0b110_100_100, body, 100, CancellationToken.None);

        Assert.Equal(5UL, written.Value);
        Assert.Equal("hello", ledger.Files.Find("alice", "notes/up.txt")!.Content);
    }

    /// <summary>A streamed write above its cap is refused as invalid input and stores nothing.</summary>
    [Fact]
    public async Task A_streamed_write_above_its_cap_is_refused_and_stores_nothing()
    {
        var (client, ledger) = NewClient();
        using var body = new MemoryStream(Encoding.UTF8.GetBytes("hello"));

        var written = await client.WriteFileStreamAsync(
            "alice", "up.txt", 0b110_100_100, body, 4, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, written.Error!.Code);
        Assert.Null(ledger.Files.Find("alice", "up.txt"));
    }

    /// <summary>Writing under directories that do not exist creates them.</summary>
    [Fact]
    public async Task Writing_under_directories_that_do_not_exist_creates_them()
    {
        var (client, ledger) = NewClient();

        await client.WriteFileAsync("alice", "a/b/c.txt", "x", 0b110_100_100, CancellationToken.None);

        Assert.True(ledger.Files.Find("alice", "a")!.IsDirectory);
        Assert.True(ledger.Files.Find("alice", "a/b")!.IsDirectory);
        Assert.False(ledger.Files.Find("alice", "a/b/c.txt")!.IsDirectory);
    }

    /// <summary>A path is read the same with redundant separators and dot segments.</summary>
    [Fact]
    public async Task A_path_is_read_the_same_with_redundant_separators_and_dot_segments()
    {
        var (client, ledger) = NewClient();

        await client.WriteFileAsync("alice", "a//./b.txt", "x", 0, CancellationToken.None);

        Assert.NotNull(ledger.Files.Find("alice", "a/b.txt"));
    }

    /// <summary>Overwriting a file replaces its content and accounts for the difference in bytes.</summary>
    [Fact]
    public async Task Overwriting_a_file_replaces_its_content_and_accounts_for_the_difference_in_bytes()
    {
        var (client, ledger) = NewClient();
        await client.WriteFileAsync("alice", "f.txt", "long content", 0, CancellationToken.None);

        await client.WriteFileAsync("alice", "f.txt", "ab", 0, CancellationToken.None);

        Assert.Equal("ab", ledger.Files.Find("alice", "f.txt")!.Content);
        Assert.Equal(2L, ledger.Files.BytesHeldBy("alice"));
    }

    /// <summary>Only the twelve permission bits of a mode are kept.</summary>
    [Fact]
    public async Task Only_the_twelve_permission_bits_of_a_mode_are_kept()
    {
        var (client, ledger) = NewClient();

        await client.WriteFileAsync("alice", "f.txt", "x", 0xFFFF_FFFF, CancellationToken.None);

        Assert.Equal(0xFFFu, ledger.Files.Find("alice", "f.txt")!.Mode);
    }

    /// <summary>
    /// A path that leaves the home, names the home itself, or carries a control character is invalid input.
    /// </summary>
    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("a/../../escape.txt")]
    [InlineData("a/..")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("./")]
    [InlineData("a\0b")]
    [InlineData("a\nb")]
    public async Task A_path_that_leaves_the_home_or_names_nothing_is_invalid_input(string path)
    {
        var (client, ledger) = NewClient();

        var written = await client.WriteFileAsync("alice", path, "x", 0, CancellationToken.None);
        var deleted = await client.DeleteEntryAsync("alice", path, true, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, written.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, deleted.Error!.Code);
        Assert.Equal(0L, ledger.Files.BytesHeldBy("alice"));
    }

    /// <summary>A path longer than the ceiling is invalid input.</summary>
    [Fact]
    public async Task A_path_longer_than_the_ceiling_is_invalid_input()
    {
        var (client, _) = NewClient();
        var path = new string('a', SimulatedAgentLimits.Default.MaxFilePathLength + 1);

        var written = await client.WriteFileAsync("alice", path, "x", 0, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, written.Error!.Code);
    }

    /// <summary>A file cannot be written over a directory, and a directory cannot be made inside a file.</summary>
    [Fact]
    public async Task A_file_cannot_be_written_over_a_directory_nor_a_directory_made_inside_a_file()
    {
        var (client, _) = NewClient();
        await client.WriteFileAsync("alice", "dir/f.txt", "x", 0, CancellationToken.None);

        var overDirectory = await client.WriteFileAsync("alice", "dir", "x", 0, CancellationToken.None);
        var insideFile = await client.WriteFileAsync("alice", "dir/f.txt/deeper.txt", "x", 0, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, overDirectory.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, insideFile.Error!.Code);
    }

    /// <summary>Two accounts may hold a file at the same path without seeing each other's.</summary>
    [Fact]
    public async Task Two_accounts_may_hold_a_file_at_the_same_path_without_seeing_each_others()
    {
        var (client, ledger) = NewClient();
        await client.WriteFileAsync("alice", "f.txt", "alice's", 0, CancellationToken.None);
        await client.WriteFileAsync("bob", "f.txt", "bob's", 0, CancellationToken.None);
        await client.DeleteEntryAsync("alice", "f.txt", false, CancellationToken.None);

        Assert.Null(ledger.Files.Find("alice", "f.txt"));
        Assert.Equal("bob's", ledger.Files.Find("bob", "f.txt")!.Content);
    }

    /// <summary>Content that looks like markup or a program is stored as the text it is.</summary>
    [Fact]
    public async Task Content_that_looks_like_markup_or_a_program_is_stored_as_the_text_it_is()
    {
        var (client, ledger) = NewClient();
        var hostile = "<script>alert(1)</script>\n#!/bin/sh\nrm -rf /\n";

        await client.WriteFileAsync("alice", "index.html", hostile, 0b111_101_101, CancellationToken.None);

        Assert.Equal(hostile, ledger.Files.Find("alice", "index.html")!.Content);
    }

    /// <summary>Deleting a file removes it and frees its bytes; a second delete answers not found.</summary>
    [Fact]
    public async Task Deleting_a_file_removes_it_and_frees_its_bytes_and_a_second_delete_answers_not_found()
    {
        var (client, ledger) = NewClient();
        await client.WriteFileAsync("alice", "f.txt", "12345", 0, CancellationToken.None);

        var deleted = await client.DeleteEntryAsync("alice", "f.txt", false, CancellationToken.None);
        var again = await client.DeleteEntryAsync("alice", "f.txt", false, CancellationToken.None);

        Assert.True(deleted.IsSuccess);
        Assert.Equal(0L, ledger.Files.BytesHeldBy("alice"));
        Assert.Equal(SimulatedAgentErrors.NotFoundCode, again.Error!.Code);
    }

    /// <summary>
    /// A directory that still holds entries is removed only recursively, and then with everything under it.
    /// </summary>
    [Fact]
    public async Task A_non_empty_directory_is_removed_only_recursively_and_with_everything_under_it()
    {
        var (client, ledger) = NewClient();
        await client.WriteFileAsync("alice", "dir/sub/f.txt", "123", 0, CancellationToken.None);
        await client.WriteFileAsync("alice", "dirx/keep.txt", "keep", 0, CancellationToken.None);

        var refused = await client.DeleteEntryAsync("alice", "dir", false, CancellationToken.None);
        var removed = await client.DeleteEntryAsync("alice", "dir", true, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, refused.Error!.Code);
        Assert.True(removed.IsSuccess);
        Assert.Null(ledger.Files.Find("alice", "dir"));
        Assert.Null(ledger.Files.Find("alice", "dir/sub/f.txt"));
        Assert.NotNull(ledger.Files.Find("alice", "dirx/keep.txt"));
        Assert.Equal(4L, ledger.Files.BytesHeldBy("alice"));
    }

    /// <summary>
    /// An account that would hold more bytes than the ceiling is refused as too full and nothing is written.
    /// </summary>
    [Fact]
    public async Task An_account_that_would_exceed_its_byte_ceiling_is_refused_as_too_full_and_nothing_is_written()
    {
        var (client, ledger) = NewClient();
        var half = new string('a', (int)(SimulatedAgentLimits.Default.MaxFileBytesPerAccount / 2));
        await client.WriteFileAsync("alice", "one", half, 0, CancellationToken.None);
        await client.WriteFileAsync("alice", "two", half, 0, CancellationToken.None);

        var over = await client.WriteFileAsync("alice", "three", "x", 0, CancellationToken.None);
        var grown = await client.WriteFileAsync("alice", "two", half + "x", 0, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.BelowFreeSpaceFloorCode, over.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.BelowFreeSpaceFloorCode, grown.Error!.Code);
        Assert.Null(ledger.Files.Find("alice", "three"));
        Assert.Equal(half, ledger.Files.Find("alice", "two")!.Content);
    }

    /// <summary>An account at its ceiling does not stop another account writing.</summary>
    [Fact]
    public async Task An_account_at_its_ceiling_does_not_stop_another_account_writing()
    {
        var (client, _) = NewClient();
        var full = new string('a', (int)SimulatedAgentLimits.Default.MaxFileBytesPerAccount);
        await client.WriteFileAsync("alice", "all", full, 0, CancellationToken.None);

        var bob = await client.WriteFileAsync("bob", "f", "x", 0, CancellationToken.None);

        Assert.True(bob.IsSuccess);
    }

    /// <summary>When the whole server holds as many bytes as it will, any account is refused as too full.</summary>
    [Fact]
    public async Task When_the_server_holds_as_many_bytes_as_it_will_any_account_is_refused_as_too_full()
    {
        var (client, _) = NewClient();
        var full = new string('a', (int)SimulatedAgentLimits.Default.MaxFileBytesPerAccount);
        var accounts = (int)(SimulatedAgentLimits.Default.MaxFileBytesTotal
            / SimulatedAgentLimits.Default.MaxFileBytesPerAccount);
        for (var index = 0; index < accounts; index++)
        {
            Assert.True(
                (await client.WriteFileAsync($"acct{index}", "all", full, 0, CancellationToken.None)).IsSuccess);
        }

        var over = await client.WriteFileAsync("newcomer", "f", "x", 0, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.BelowFreeSpaceFloorCode, over.Error!.Code);
    }

    /// <summary>Once the server holds as many entries as it will, writing another answers limit reached.</summary>
    [Fact]
    public async Task Writing_beyond_the_entry_ceiling_answers_limit_reached()
    {
        var (client, _) = NewClient();
        for (var index = 0; index < SimulatedAgentLimits.Default.MaxFileEntries; index++)
        {
            Assert.True(
                (await client.WriteFileAsync("alice", $"f{index}", string.Empty, 0, CancellationToken.None)).IsSuccess);
        }

        var over = await client.WriteFileAsync("alice", "one-too-many", string.Empty, 0, CancellationToken.None);
        var overwrite = await client.WriteFileAsync("alice", "f0", "now it has content", 0, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.LimitReachedCode, over.Error!.Code);
        Assert.True(overwrite.IsSuccess);
    }

    /// <summary>The bytes counted for a multi-byte file match the bytes the write reported.</summary>
    [Fact]
    public async Task The_bytes_counted_for_a_file_match_the_bytes_the_write_reported()
    {
        var (client, ledger) = NewClient();
        var content = "\u00e9\u00e9\u00e9";

        var written = await client.WriteFileAsync("alice", "f", content, 0, CancellationToken.None);

        Assert.Equal((ulong)Encoding.UTF8.GetByteCount(content), written.Value);
        Assert.Equal((long)written.Value, ledger.Files.BytesHeldBy("alice"));
    }

    /// <summary>Every operation that changes a tree the simulated server does not model answers not implemented.</summary>
    [Fact]
    public async Task Every_tree_changing_operation_the_simulation_does_not_model_answers_not_implemented()
    {
        var (client, _) = NewClient();
        var token = CancellationToken.None;

        var codes = new[]
        {
            (await client.CreateFileAsync("alice", "p", 0, token)).Error,
            (await client.CreateDirectoryAsync("alice", "p", 0, token)).Error,
            (await client.MoveEntryAsync("alice", "p", "q", token)).Error,
            (await client.CopyEntryAsync("alice", "p", "q", 1, 1, token)).Error,
            (await client.ChangePermissionsAsync("alice", "p", 0, false, 0, token)).Error,
            (await client.TrashEntryAsync("alice", "p", token)).Error,
            (await client.ListTrashAsync("alice", token)).Error,
            (await client.RestoreTrashEntryAsync("alice", "id", string.Empty, token)).Error,
            (await client.EmptyTrashAsync("alice", token)).Error,
            (await client.PurgeExpiredTrashAsync("alice", 1, token)).Error,
            (await client.ForceDeleteEntryAsync("alice", "p", token)).Error,
            (await client.CreateArchiveAsync("alice", ["p"], "q", AgentArchiveFormat.Zip, 1, 1, token)).Error,
            (await client.ExtractArchiveAsync("alice", "p", "q", AgentArchiveFormat.Zip, 1, 1, 1, token)).Error,
        };

        Assert.Equal(13, codes.Length);
        Assert.All(codes, error => { Assert.Equal(SimulatedAgentErrors.NotImplementedCode, error!.Code); });
    }

    /// <summary>Builds a client over a fresh ledger and returns both.</summary>
    /// <returns>The client and the ledger it reads, so a test can inspect the state.</returns>
    private static (SimulatedAgentFilesClient Client, SimulatedAgentLedger Ledger) NewClient()
    {
        var ledger = new SimulatedAgentLedger(SimulatedAgentLimits.Default);

        return (new SimulatedAgentFilesClient(ledger), ledger);
    }
}
