namespace Maran.Agent.Client.Services.BackupService;

/// <summary>What an archive says it holds.</summary>
/// <remarks>
/// Nothing here is a path, on the agent's side and therefore on this one: an archive carrying the
/// absolute paths it was made from would be a map of the host for whoever downloads it.
/// </remarks>
/// <param name="Version">The layout version of the document.</param>
/// <param name="Account">The account whose home and databases the archive holds.</param>
/// <param name="BackupId">The backup's id, which is also the artifact's file name stem.</param>
/// <param name="CreatedAtUnix">When the creation started, in seconds since the Unix epoch.</param>
/// <param name="HomeBytes">The measured size of the account's home at that moment, in bytes.</param>
/// <param name="Databases">One entry per dumped database, in the order they were dumped.</param>
/// <param name="AgentVersion">
/// The version of the agent that wrote the archive, for an operator reading an old artifact. A
/// restore never consults it.
/// </param>
public sealed record AgentBackupManifest(
    uint Version,
    string Account,
    string BackupId,
    long CreatedAtUnix,
    ulong HomeBytes,
    IReadOnlyList<AgentManifestDatabase> Databases,
    string AgentVersion);
