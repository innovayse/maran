namespace Maran.Modules.Backups.Queries.CountConfiguredBackupDestinations;

/// <summary>
/// Counts the ways an administrator has configured where and when this server's backups go, beyond
/// what a fresh installation ships with: a destination other than the seeded local one, and the
/// host-wide schedule when it is switched on. For the first-run checklist.
/// </summary>
/// <remarks>
/// <para>
/// The seeded local destination exists on a server nobody has touched, so counting destinations
/// would tick the checklist's backup step on its own. A schedule that is saved but switched off
/// backs nothing up and is not counted, and neither is one account's own override: that is a
/// customer's setting, not the administrator's answer to "where do backups go".
/// </para>
/// <para>
/// Dispatched by the panel itself, never bound from a request, so it carries no validator.
/// </para>
/// </remarks>
public sealed record CountConfiguredBackupDestinationsQuery;
