namespace Maran.Host.Dashboard;

/// <summary>How much of each thing this server holds.</summary>
/// <remarks>
/// <para>
/// Counts rather than lists: the landing page says how many and links to the screen that owns the
/// detail. A first screen that embedded three tables would be three screens badly, and every one of
/// them already exists and is better.
/// </para>
/// <para>
/// <b>Scheduled tasks are deliberately NOT counted here, and the reason is worth writing down.</b>
/// The Cron module keeps no table of its own — a crontab lives on the host and the agent reads it
/// for ONE account at a time, by that account's system user name, because that is what makes the
/// isolation the operating system's rather than a prefix the panel hopes nobody shares. A
/// server-wide count would therefore be one privileged agent round-trip per account, making the
/// landing page the most expensive screen in the panel and the slowest on the largest servers. The
/// three counts below are single database reads. Counting cron entries needs either a panel-side
/// projection of the crontabs or an agent call that counts across accounts; neither exists, both
/// are real work, and inventing a fan-out here would be the kind of thing that looks free until a
/// server has two hundred accounts.
/// </para>
/// </remarks>
/// <param name="Accounts">Hosting accounts on this server.</param>
/// <param name="Sites">Sites across every account.</param>
/// <param name="Databases">Databases across every account.</param>
public sealed record DashboardCountsDto(
    int Accounts,
    int Sites,
    int Databases);
