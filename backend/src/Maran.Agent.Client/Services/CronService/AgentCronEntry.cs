namespace Maran.Agent.Client.Services.CronService;

/// <summary>One scheduled task installed in an account's crontab.</summary>
/// <param name="EntryId">
/// The agent's own identifier for this entry, stable across listings and the only way to target an
/// update, a deletion or an enablement change.
/// </param>
/// <param name="Schedule">When the entry runs.</param>
/// <param name="Command">
/// The command line exactly as the account owner wrote it. It runs under the account's own uid, so
/// it grants no privilege the account does not already have over SFTP.
/// </param>
/// <param name="Enabled">
/// True when the entry is a live crontab line. A disabled entry stays in the crontab commented out,
/// so disabling never loses it.
/// </param>
/// <remarks>
/// The wire message also carries <c>last_exit_code</c> and <c>last_run_at_unix</c>, and this type
/// deliberately drops both. The agent writes them as 0 in a listing and says so: reading an entry's
/// exit status means one privileged read per entry under the account's home, which would turn one
/// listing into N of them. Carrying the zeros here would put "the last run succeeded, at the epoch"
/// in front of a customer for an entry that has never run. Ask <c>GetEntryOutputAsync</c> for the
/// entry being shown.
/// </remarks>
public sealed record AgentCronEntry(
    string EntryId,
    AgentCronSchedule Schedule,
    string Command,
    bool Enabled);
