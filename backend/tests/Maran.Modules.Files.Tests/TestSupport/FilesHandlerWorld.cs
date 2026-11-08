using Maran.Modules.Files.Options;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Files.Tests.TestSupport;

/// <summary>
/// The collaborators every Files handler is built from, wired the way the module wires them, with the agent
/// replaced by a recording fake and the audit writer by one that keeps what it was given.
/// </summary>
public sealed class FilesHandlerWorld
{
    /// <summary>The origin address every command in these tests carries.</summary>
    public const string Address = "203.0.113.9";

    /// <summary>The user agent every command in these tests carries.</summary>
    public const string UserAgent = "tests";

    /// <summary>Gets the active account the signed-in caller owns.</summary>
    public AccountSnapshot Active { get; }

    /// <summary>Gets a suspended account the directory also knows.</summary>
    public AccountSnapshot Suspended { get; }

    /// <summary>Gets an account id the directory does not know, as another tenant's id behaves.</summary>
    public Guid Stranger { get; } = Guid.NewGuid();

    /// <summary>Gets the recording fake that stands in for the agent.</summary>
    public RecordingAgentFilesClient Client { get; } = new();

    /// <summary>Gets what the audit journal wrote.</summary>
    public RecordingAuditWriter Writer { get; } = new();

    /// <summary>Gets the limits the handlers read.</summary>
    public FilesOptions Limits { get; }

    /// <summary>Gets the options monitor over <see cref="Limits"/>.</summary>
    public FixedOptionsMonitor<FilesOptions> Monitor { get; }

    /// <summary>Gets the account directory the gate resolves through.</summary>
    public StubAccountDirectory Directory { get; }

    /// <summary>Gets the module's audit journal.</summary>
    public FilesAuditJournal Journal { get; }

    /// <summary>Gets the account gate every handler calls first.</summary>
    public FilesAccountGate Gate { get; }

    /// <summary>Gets the batch runner.</summary>
    public FilesBatchRunner Runner { get; }

    /// <summary>Gets the single-entry lookup.</summary>
    public FilesEntryLookup Lookup { get; }

    /// <summary>Gets the entry reader.</summary>
    public FilesEntryReader Reader { get; }

    /// <summary>Gets the localized kind names.</summary>
    public FilesDisplayNames Names { get; }

    /// <summary>Gets the transfer slots shared by the handlers built from this world.</summary>
    public FilesTransferSlots Slots { get; }

    /// <summary>Wires the collaborators for one kind of caller.</summary>
    /// <param name="administrator">Whether the caller is an administrator rather than the account's customer.</param>
    /// <param name="limits">Limits to use instead of the defaults.</param>
    private FilesHandlerWorld(bool administrator, FilesOptions? limits)
    {
        Active = FilesTestAccounts.Account(Guid.NewGuid(), "alpha", AccountState.Active);
        Suspended = FilesTestAccounts.Account(Guid.NewGuid(), "beta", AccountState.Suspended);
        Limits = limits ?? new FilesOptions();
        Monitor = new FixedOptionsMonitor<FilesOptions>(Limits);

        Caller caller = administrator
            ? new AdministratorCaller(Guid.NewGuid(), "root")
            : new CustomerCaller(Guid.NewGuid(), "owner", Active.Id);
        ICurrentUser user = new FakeCurrentUser(caller);

        Directory = new StubAccountDirectory(Active, Suspended);
        Journal = new FilesAuditJournal(Writer, user);
        Gate = new FilesAccountGate(Directory, user, Journal);
        Runner = new FilesBatchRunner(Journal, new StubErrorTextProvider(), Monitor);
        Lookup = new FilesEntryLookup(Client, NullLogger<FilesEntryLookup>.Instance);
        Names = new FilesDisplayNames(new KeyStringLocalizer<DisplayNames>());
        Reader = new FilesEntryReader(Lookup, Names);
        Slots = new FilesTransferSlots(Monitor);
    }

    /// <summary>Builds a world in which the caller is the customer who owns <see cref="Active"/>.</summary>
    /// <param name="limits">Limits to use instead of the defaults.</param>
    /// <returns>The world.</returns>
    public static FilesHandlerWorld ForCustomer(FilesOptions? limits = null)
    {
        return new FilesHandlerWorld(administrator: false, limits);
    }

    /// <summary>Builds a world in which the caller is an administrator.</summary>
    /// <param name="limits">Limits to use instead of the defaults.</param>
    /// <returns>The world.</returns>
    public static FilesHandlerWorld ForAdministrator(FilesOptions? limits = null)
    {
        return new FilesHandlerWorld(administrator: true, limits);
    }
}
