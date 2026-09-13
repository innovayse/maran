namespace Maran.Agent.Client.Services.BackupService;

/// <summary>One database's line in a backup's manifest.</summary>
/// <param name="Name">The database's full name, prefix included, exactly as it was dumped.</param>
/// <param name="Bytes">The size of the dump file, in bytes.</param>
/// <param name="Sha256">
/// SHA-256 of the dump file, hex, lowercase. The load-bearing field: a restore checks each extracted
/// dump against it before loading, because the loader connects as the database superuser.
/// </param>
public sealed record AgentManifestDatabase(string Name, ulong Bytes, string Sha256);
