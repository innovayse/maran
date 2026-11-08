namespace Maran.Modules.Files.Jobs;

/// <summary>
/// Asks for one pass that removes, for every account on the host, the trash entries older than the
/// configured retention.
/// </summary>
/// <remarks>
/// Published once a day by the Host's scheduler, which owns the cadence; this module owns the job. It
/// names no account and no age: the pass lists the accounts when it runs and reads the retention from
/// <c>FilesOptions</c> when it runs, so a request that sat in the queue acts on the host as it is now.
/// It carries no secret and travels the panel's local, in-memory queue.
/// </remarks>
public sealed record TrashRetentionRequested;
