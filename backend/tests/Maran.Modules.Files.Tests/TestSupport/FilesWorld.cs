using Maran.Agent.Client.Services.FilesService;
using Maran.Modules.Files.Models;
using Maran.Modules.Files.Options;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Files.Tests.TestSupport;

/// <summary>
/// Everything a Files handler is built from, wired the way the module wires it, over a directory that
/// knows ONE account and a recording agent: the caller owns that account and no other, so any other id is
/// a tenant the directory will not show.
/// </summary>
public sealed class FilesWorld
{
    /// <summary>The account the caller owns.</summary>
    public static readonly Guid OwnAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    /// <summary>An account of another tenant: the directory answers nothing for it.</summary>
    public static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>When every entry in the world was last modified.</summary>
    public static readonly DateTimeOffset Modified = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Where the call came from, for every entry.</summary>
    public static readonly FilesCallOrigin Origin = new("203.0.113.9", "tests");

    /// <summary>The system user name of the account the caller owns.</summary>
    public const string Username = "alice";

    /// <summary>The limits in force.</summary>
    public FilesOptions Limits { get; }

    /// <summary>The limits as the handlers read them.</summary>
    public IOptionsMonitor<FilesOptions> Options { get; }

    /// <summary>What the journal wrote.</summary>
    public RecordingAuditWriter Writer { get; }

    /// <summary>The module's journal.</summary>
    public FilesAuditJournal Journal { get; }

    /// <summary>The module's gate.</summary>
    public FilesAccountGate Gate { get; }

    /// <summary>The recording agent.</summary>
    public RecordingAgentFilesClient Agent { get; }

    /// <summary>The module's entry lookup.</summary>
    public FilesEntryLookup Lookup { get; }

    /// <summary>The module's kind names.</summary>
    public FilesDisplayNames Names { get; }

    /// <summary>The module's transfer slots.</summary>
    public FilesTransferSlots Slots { get; }

    /// <summary>Creates the world.</summary>
    /// <param name="state">Whether the caller's account is in service or suspended.</param>
    /// <param name="administrator">Whether the caller is an administrator rather than the account's customer.</param>
    /// <param name="options">The limits; the defaults when null.</param>
    public FilesWorld(AccountState state = AccountState.Active, bool administrator = false, FilesOptions? options = null)
    {
        Limits = options ?? new FilesOptions();
        var monitor = new FixedOptionsMonitor<FilesOptions>(Limits);

        Caller caller = administrator
            ? new AdministratorCaller(Guid.NewGuid(), "root")
            : new CustomerCaller(Guid.NewGuid(), "owner", OwnAccountId);
        var currentUser = new FakeCurrentUser(caller);

        Writer = new RecordingAuditWriter();
        Journal = new FilesAuditJournal(Writer, currentUser);
        Gate = new FilesAccountGate(
            new StubAccountDirectory(FilesTestAccounts.Account(OwnAccountId, Username, state)), currentUser, Journal);
        Agent = new RecordingAgentFilesClient();
        Lookup = new FilesEntryLookup(Agent, NullLogger<FilesEntryLookup>.Instance);
        Names = new FilesDisplayNames(new KeyStringLocalizer<DisplayNames>());
        Slots = new FilesTransferSlots(monitor);
        Options = monitor;
    }

    /// <summary>A regular file, as the agent lists one.</summary>
    /// <param name="name">The file's name.</param>
    /// <param name="size">Its size in bytes.</param>
    /// <param name="mode">Its permission bits.</param>
    /// <returns>The entry.</returns>
    public static FileEntryDto File(string name, ulong size = 10, uint mode = 0b110_100_100)
    {
        return new FileEntryDto(name, FileEntryKind.File, size, mode, Modified, 1000, 1000, 1);
    }

    /// <summary>A folder, as the agent lists one.</summary>
    /// <param name="name">The folder's name.</param>
    /// <returns>The entry.</returns>
    public static FileEntryDto Folder(string name)
    {
        return new FileEntryDto(name, FileEntryKind.Directory, 0, 0b111_101_101, Modified, 1000, 1000, 2);
    }
}
