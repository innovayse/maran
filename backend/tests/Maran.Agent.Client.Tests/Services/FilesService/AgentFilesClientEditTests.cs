using Google.Protobuf;
using Maran.Agent.Client.Services.FilesService;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.FilesService;

/// <summary>
/// Mapping contract of the operations of AgentFilesClient that change a customer's tree: what each
/// request carries, how each success reads, and that a refusal or an empty answer is never a success.
/// </summary>
public sealed class AgentFilesClientEditTests
{
    /// <summary>The source paths the archive test sends and expects back on the wire.</summary>
    private static readonly string[] ArchiveSources = ["a", "b/c"];

    /// <summary>Creating a file sends an empty body that refuses to replace anything.</summary>
    [Fact]
    public async Task Creating_a_file_sends_an_empty_body_that_refuses_to_replace_anything()
    {
        var stub = new StubFilesService
        {
            WriteResponse = new WriteFileResponse { Ok = new WriteFileOk { BytesWritten = 0 } },
        };

        var result = await ClientFor(stub).CreateFileAsync(
            "acct", "docs/new.txt", 0b110_100_100, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.IsType<WriteFileRequest>(stub.LastWriteRequest);
        Assert.Equal("acct", request.Header.AccountUsername);
        Assert.Equal("docs/new.txt", request.Header.Path);
        Assert.Equal(0b110_100_100u, request.Header.Mode);
        Assert.True(request.Header.FailIfExists);
        Assert.Equal(ByteString.Empty, request.Chunk);
    }

    /// <summary>Creating a file over an existing name maps to the already-exists code.</summary>
    [Fact]
    public async Task Creating_a_file_over_an_existing_name_maps_to_the_already_exists_code()
    {
        var stub = new StubFilesService
        {
            WriteResponse = new WriteFileResponse { Error = Refusal(ErrorCode.AlreadyExists) },
        };

        var result = await ClientFor(stub).CreateFileAsync("acct", "p", 0, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentAlreadyExists", result.Error!.Code);
    }

    /// <summary>Creating a directory sends the account the path and the mode.</summary>
    [Fact]
    public async Task Creating_a_directory_sends_the_account_the_path_and_the_mode()
    {
        var stub = new StubFilesService
        {
            CreateDirectoryResponse = new CreateDirectoryResponse { Ok = new CreateDirectoryOk() },
        };

        var result = await ClientFor(stub).CreateDirectoryAsync(
            "acct", "a/b", 0b111_101_101, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("acct", stub.LastCreateDirectoryRequest!.AccountUsername);
        Assert.Equal("a/b", stub.LastCreateDirectoryRequest.Path);
        Assert.Equal(0b111_101_101u, stub.LastCreateDirectoryRequest.Mode);
    }

    /// <summary>Moving an entry sends both paths and a refusal carries only a code.</summary>
    [Fact]
    public async Task Moving_an_entry_sends_both_paths_and_a_refusal_carries_only_a_code()
    {
        var stub = new StubFilesService
        {
            MoveEntryResponse = new MoveEntryResponse { Error = Refusal(ErrorCode.NotFound) },
        };

        var result = await ClientFor(stub).MoveEntryAsync("acct", "a.txt", "b/a.txt", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
        Assert.Equal("a.txt", stub.LastMoveEntryRequest!.SourcePath);
        Assert.Equal("b/a.txt", stub.LastMoveEntryRequest.DestinationPath);
    }

    /// <summary>Copying an entry sends the bounds the caller states.</summary>
    [Fact]
    public async Task Copying_an_entry_sends_the_bounds_the_caller_states()
    {
        var stub = new StubFilesService
        {
            CopyEntryResponse = new CopyEntryResponse { Ok = new CopyEntryOk { Entries = 3, Bytes = 10 } },
        };

        var result = await ClientFor(stub).CopyEntryAsync("acct", "a", "b", 500, 4096, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("a", stub.LastCopyEntryRequest!.SourcePath);
        Assert.Equal("b", stub.LastCopyEntryRequest.DestinationPath);
        Assert.Equal(500ul, stub.LastCopyEntryRequest.MaxEntries);
        Assert.Equal(4096ul, stub.LastCopyEntryRequest.MaxBytes);
    }

    /// <summary>A copy that reached a bound maps to the limit-reached code.</summary>
    [Fact]
    public async Task A_copy_that_reached_a_bound_maps_to_the_limit_reached_code()
    {
        var stub = new StubFilesService
        {
            CopyEntryResponse = new CopyEntryResponse { Error = Refusal(ErrorCode.LimitReached) },
        };

        var result = await ClientFor(stub).CopyEntryAsync("acct", "a", "b", 1, 1, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentLimitReached", result.Error!.Code);
    }

    /// <summary>Changing permissions reports how many entries changed and how many were skipped.</summary>
    [Fact]
    public async Task Changing_permissions_reports_how_many_entries_changed_and_how_many_were_skipped()
    {
        var stub = new StubFilesService
        {
            ChangePermissionsResponse = new ChangePermissionsResponse
            {
                Ok = new ChangePermissionsOk { Changed = 7, Skipped = 2 },
            },
        };

        var result = await ClientFor(stub).ChangePermissionsAsync(
            "acct", "site", 0b110_100_100, recursive: true, 0b111_101_101, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(7ul, result.Value.Changed);
        Assert.Equal(2ul, result.Value.Skipped);
        var request = stub.LastChangePermissionsRequest!;
        Assert.Equal(0b110_100_100u, request.Mode);
        Assert.True(request.Recursive);
        Assert.Equal(0b111_101_101u, request.DirectoryMode);
    }

    /// <summary>Trashing an entry answers the identifier it has in the trash.</summary>
    [Fact]
    public async Task Trashing_an_entry_answers_the_identifier_it_has_in_the_trash()
    {
        var stub = new StubFilesService
        {
            TrashEntryResponse = new TrashEntryResponse
            {
                Ok = new TrashEntryOk { TrashId = "1700000000-abcdef0123456789" },
            },
        };

        var result = await ClientFor(stub).TrashEntryAsync("acct", "old.txt", CancellationToken.None);

        Assert.Equal("1700000000-abcdef0123456789", result.Value);
        Assert.Equal("old.txt", stub.LastTrashEntryRequest!.Path);
    }

    /// <summary>Listing the trash reads every field of every entry and keeps the agent's order.</summary>
    [Fact]
    public async Task Listing_the_trash_reads_every_field_of_every_entry_and_keeps_the_agents_order()
    {
        var stub = new StubFilesService
        {
            ListTrashResponse = new ListTrashResponse
            {
                Ok = new ListTrashOk
                {
                    Entries =
                    {
                        new TrashedEntry
                        {
                            TrashId = "1700000100-aa",
                            OriginalPath = "a/new.txt",
                            TrashedAtUnix = 1_700_000_100,
                            SizeBytes = 12,
                        },
                        new TrashedEntry
                        {
                            TrashId = "1700000000-bb",
                            OriginalPath = "a/dir",
                            TrashedAtUnix = 1_700_000_000,
                            IsDirectory = true,
                        },
                    },
                },
            },
        };

        var result = await ClientFor(stub).ListTrashAsync("acct", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        var first = result.Value[0];
        Assert.Equal("1700000100-aa", first.TrashId);
        Assert.Equal("a/new.txt", first.OriginalPath);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_100), first.TrashedAt);
        Assert.False(first.IsDirectory);
        Assert.Equal(12ul, first.SizeBytes);
        Assert.True(result.Value[1].IsDirectory);
        Assert.Equal("acct", stub.LastListTrashRequest!.AccountUsername);
    }

    /// <summary>A trashing time outside what a date can hold is clamped rather than thrown on.</summary>
    [Fact]
    public async Task A_trashing_time_outside_what_a_date_can_hold_is_clamped_rather_than_thrown_on()
    {
        var stub = new StubFilesService
        {
            ListTrashResponse = new ListTrashResponse
            {
                Ok = new ListTrashOk
                {
                    Entries = { new TrashedEntry { TrashId = "x", TrashedAtUnix = long.MaxValue } },
                },
            },
        };

        var result = await ClientFor(stub).ListTrashAsync("acct", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(253_402_300_799), result.Value[0].TrashedAt);
    }

    /// <summary>Restoring an entry sends the id and the destination and answers the restored path.</summary>
    [Fact]
    public async Task Restoring_an_entry_sends_the_id_and_the_destination_and_answers_the_restored_path()
    {
        var stub = new StubFilesService
        {
            RestoreTrashEntryResponse = new RestoreTrashEntryResponse
            {
                Ok = new RestoreTrashEntryOk { RestoredPath = "a/old.txt" },
            },
        };

        var result = await ClientFor(stub).RestoreTrashEntryAsync(
            "acct", "1-aa", string.Empty, CancellationToken.None);

        Assert.Equal("a/old.txt", result.Value);
        Assert.Equal("1-aa", stub.LastRestoreTrashEntryRequest!.TrashId);
        Assert.Equal(string.Empty, stub.LastRestoreTrashEntryRequest.DestinationPath);
    }

    /// <summary>Restoring into a taken place maps to the already-exists code.</summary>
    [Fact]
    public async Task Restoring_into_a_taken_place_maps_to_the_already_exists_code()
    {
        var stub = new StubFilesService
        {
            RestoreTrashEntryResponse = new RestoreTrashEntryResponse { Error = Refusal(ErrorCode.AlreadyExists) },
        };

        var result = await ClientFor(stub).RestoreTrashEntryAsync("acct", "1-aa", "x", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentAlreadyExists", result.Error!.Code);
    }

    /// <summary>Emptying the trash answers how many entries were removed.</summary>
    [Fact]
    public async Task Emptying_the_trash_answers_how_many_entries_were_removed()
    {
        var stub = new StubFilesService
        {
            EmptyTrashResponse = new EmptyTrashResponse { Ok = new EmptyTrashOk { EntriesRemoved = 5 } },
        };

        var result = await ClientFor(stub).EmptyTrashAsync("acct", CancellationToken.None);

        Assert.Equal(5ul, result.Value);
    }

    /// <summary>Purging expired entries sends the age and answers how many were removed.</summary>
    [Fact]
    public async Task Purging_expired_entries_sends_the_age_and_answers_how_many_were_removed()
    {
        var stub = new StubFilesService
        {
            PurgeExpiredTrashResponse = new PurgeExpiredTrashResponse
            {
                Ok = new PurgeExpiredTrashOk { EntriesRemoved = 4 },
            },
        };

        var result = await ClientFor(stub).PurgeExpiredTrashAsync("acct", 30, CancellationToken.None);

        Assert.Equal(4ul, result.Value);
        Assert.Equal(30u, stub.LastPurgeExpiredTrashRequest!.OlderThanDays);
    }

    /// <summary>Force-deleting an entry sends the path and a missing path maps to not found.</summary>
    [Fact]
    public async Task Force_deleting_an_entry_sends_the_path_and_a_missing_path_maps_to_not_found()
    {
        var stub = new StubFilesService
        {
            ForceDeleteEntryResponse = new ForceDeleteEntryResponse { Error = Refusal(ErrorCode.NotFound) },
        };

        var result = await ClientFor(stub).ForceDeleteEntryAsync("acct", "gone", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
        Assert.Equal("gone", stub.LastForceDeleteEntryRequest!.Path);
    }

    /// <summary>Creating an archive sends every source and the chosen format and answers its size.</summary>
    [Theory]
    [InlineData(AgentArchiveFormat.Zip, ArchiveFormat.Zip)]
    [InlineData(AgentArchiveFormat.TarGz, ArchiveFormat.TarGz)]
    public async Task Creating_an_archive_sends_every_source_and_the_chosen_format_and_answers_its_size(
        AgentArchiveFormat format,
        ArchiveFormat wire)
    {
        var stub = new StubFilesService
        {
            CreateArchiveResponse = new CreateArchiveResponse { Ok = new CreateArchiveOk { SizeBytes = 2048 } },
        };

        var result = await ClientFor(stub).CreateArchiveAsync(
            "acct", ArchiveSources, "out.archive", format, 100, 9000, CancellationToken.None);

        Assert.Equal(2048ul, result.Value);
        var request = stub.LastCreateArchiveRequest!;
        Assert.Equal(ArchiveSources, request.SourcePaths);
        Assert.Equal("out.archive", request.DestinationPath);
        Assert.Equal(wire, request.Format);
        Assert.Equal(100ul, request.MaxEntries);
        Assert.Equal(9000ul, request.MaxSourceBytes);
    }

    /// <summary>Extracting an archive sends the three bounds and answers the entry count.</summary>
    [Fact]
    public async Task Extracting_an_archive_sends_the_three_bounds_and_answers_the_entry_count()
    {
        var stub = new StubFilesService
        {
            ExtractArchiveResponse = new ExtractArchiveResponse
            {
                Ok = new ExtractArchiveOk { EntryCount = 12 },
            },
        };

        var result = await ClientFor(stub).ExtractArchiveAsync(
            "acct", "in.zip", "out", AgentArchiveFormat.Zip, 1000, 5000, 100, CancellationToken.None);

        Assert.Equal(12u, result.Value);
        var request = stub.LastExtractArchiveRequest!;
        Assert.Equal("in.zip", request.ArchivePath);
        Assert.Equal("out", request.DestinationPath);
        Assert.Equal(ArchiveFormat.Zip, request.Format);
        Assert.Equal(1000ul, request.MaxEntries);
        Assert.Equal(5000ul, request.MaxExpandedBytes);
        Assert.Equal(100u, request.MaxRatio);
    }

    /// <summary>An archive with an unsafe member maps to the archive-unsafe code.</summary>
    [Fact]
    public async Task An_archive_with_an_unsafe_member_maps_to_the_archive_unsafe_code()
    {
        var stub = new StubFilesService
        {
            ExtractArchiveResponse = new ExtractArchiveResponse { Error = Refusal(ErrorCode.ArchiveUnsafe) },
        };

        var result = await ClientFor(stub).ExtractArchiveAsync(
            "acct", "in.zip", "out", AgentArchiveFormat.Zip, 1, 1, 1, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentArchiveUnsafe", result.Error!.Code);
    }

    /// <summary>An answer with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task An_answer_with_neither_branch_set_is_refused_rather_than_read_as_success_for_every_operation()
    {
        var client = ClientFor(new StubFilesService());
        var token = CancellationToken.None;

        var answers = new[]
        {
            (await client.CreateFileAsync("a", "p", 0, token)).Error,
            (await client.CreateDirectoryAsync("a", "p", 0, token)).Error,
            (await client.MoveEntryAsync("a", "p", "q", token)).Error,
            (await client.CopyEntryAsync("a", "p", "q", 1, 1, token)).Error,
            (await client.ChangePermissionsAsync("a", "p", 0, false, 0, token)).Error,
            (await client.TrashEntryAsync("a", "p", token)).Error,
            (await client.ListTrashAsync("a", token)).Error,
            (await client.RestoreTrashEntryAsync("a", "id", string.Empty, token)).Error,
            (await client.EmptyTrashAsync("a", token)).Error,
            (await client.PurgeExpiredTrashAsync("a", 1, token)).Error,
            (await client.ForceDeleteEntryAsync("a", "p", token)).Error,
            (await client.CreateArchiveAsync("a", ["p"], "q", AgentArchiveFormat.Zip, 1, 1, token)).Error,
            (await client.ExtractArchiveAsync("a", "p", "q", AgentArchiveFormat.Zip, 1, 1, 1, token)).Error,
        };

        Assert.All(answers, error => { Assert.Equal("AgentInvalidResponse", error!.Code); });
        Assert.Equal(13, answers.Length);
    }

    /// <summary>Builds a refusal as the agent sends it, with diagnostic text that must never travel on.</summary>
    /// <param name="code">The wire code.</param>
    /// <returns>The wire error.</returns>
    private static AgentError Refusal(ErrorCode code)
    {
        return new AgentError { Code = code, Message = "failed at /home/acct/secret" };
    }

    /// <summary>Builds the production client over a transport stub.</summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <returns>The client under test.</returns>
    private static AgentFilesClient ClientFor(StubFilesService stub)
    {
        return new AgentFilesClient(stub, NullLogger<AgentFilesClient>.Instance);
    }
}
