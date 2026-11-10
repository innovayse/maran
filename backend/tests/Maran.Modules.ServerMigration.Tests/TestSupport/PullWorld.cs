using System.Diagnostics.CodeAnalysis;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Utilities.Tokens;

namespace Maran.Modules.ServerMigration.Tests.TestSupport;

/// <summary>An exporting panel with one export and one grant for it, the pieces a pull handler needs, and a scripted agent.</summary>
public sealed class PullWorld
{
    /// <summary>The database.</summary>
    public ServerMigrationDbContext Database { get; }

    /// <summary>The scripted ledger.</summary>
    public ScriptedGrantLedger Ledger { get; } = new();

    /// <summary>The scripted agent.</summary>
    public ScriptedTransferClient Agent { get; } = new();

    /// <summary>The audit entries.</summary>
    public RecordedAuditEntries Audit { get; } = new();

    /// <summary>The panel's clock.</summary>
    public HandlerClock Clock { get; } = new();

    /// <summary>The export the grant opens.</summary>
    public AccountExport Export { get; }

    /// <summary>The grant.</summary>
    public ExportGrant Grant { get; }

    /// <summary>Writes packages over the scripted agent.</summary>
    public MigrationPackageWriter Writer { get; }

    /// <summary>The journal.</summary>
    public ServerMigrationAuditJournal Journal { get; }

    /// <summary>Builds the world.</summary>
    /// <param name="name">A name for the in-memory database.</param>
    public PullWorld(string name)
    {
        Database = InMemoryMigrationDatabase.Open(name);
        Export = new AccountExport(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "alice",
            new MigrationExportArtifact(Guid.NewGuid(), 10, new string('c', 64), 0),
            "[]",
            Guid.NewGuid(),
            Clock.UtcNow.AddMinutes(-5),
            TimeSpan.FromDays(7));
        Grant = new ExportGrant(
            Guid.NewGuid(), Export.Id, ExportGrantTokenHasher.Mint().Digest, null, Clock.UtcNow.AddMinutes(-1), TimeSpan.FromMinutes(30));
        Database.AccountExports.Add(Export);
        Database.ExportGrants.Add(Grant);
        Database.SaveChanges();

        Journal = new ServerMigrationAuditJournal(Audit, new AdministratorUser());
        Writer = new MigrationPackageWriter(Agent, new MigrationSigningKeys(Database, Clock), new NoHostName(), Clock);
    }

    /// <summary>A panel with no host name of its own.</summary>
    private sealed class NoHostName : IPanelHostName
    {
        /// <inheritdoc />
        public bool TryGet([NotNullWhen(true)] out string? hostName)
        {
            hostName = null;

            return false;
        }
    }
}
