namespace Maran.Agent.Client.Services.BackupService;

/// <summary>One event from a create stream: progress, or the way the stream ended.</summary>
/// <param name="Kind">Whether this is progress or one of the six terminal endings.</param>
/// <param name="Percent">Completion from 0 to 100 for progress events; zero otherwise.</param>
/// <param name="Stage">
/// Machine-stable stage id for progress events — <c>archiving_files</c>, <c>dumping_databases</c>,
/// <c>uploading</c>; empty otherwise.
/// </param>
/// <param name="SizeBytes">Size of the stored artifact on <see cref="BackupCreateEventKind.Created"/>; zero otherwise.</param>
/// <param name="Sha256">
/// SHA-256 of the stored artifact on <see cref="BackupCreateEventKind.Created"/>, hex and lowercase;
/// empty otherwise. This is the value a later restore hands back as its expected digest, so the
/// panel must store it exactly as it arrives.
/// </param>
/// <param name="DatabaseCount">
/// How many databases the archive holds a dump of on <see cref="BackupCreateEventKind.Created"/>;
/// zero otherwise. Zero is also an ordinary answer for a created backup: most static sites have none.
/// </param>
/// <param name="ErrorCode">
/// The machine-stable error code for <see cref="BackupCreateEventKind.Failed"/>; null otherwise. It
/// is a code and never the agent's own sentence, which can name absolute paths on the host.
/// </param>
public sealed record BackupCreateEvent(
    BackupCreateEventKind Kind,
    uint Percent,
    string Stage,
    ulong SizeBytes,
    string Sha256,
    ulong DatabaseCount,
    string? ErrorCode);
