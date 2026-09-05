namespace Maran.Modules.Cron.Commands.SetCronEntryEnabled;

/// <summary>
/// Switches one cron entry on or off without touching its schedule or its command.
/// </summary>
/// <remarks>
/// Its own operation, and its own audit action, rather than a field on the update: a disabled entry
/// that still fires — or an enabled one that does not — is the failure an operator needs to be able
/// to date, and folding the flag into an edit would make that date the date of an unrelated change.
///
/// Disabling keeps the entry in the crontab, commented out, so switching one off never loses it.
/// </remarks>
/// <param name="AccountId">
/// The account whose crontab holds the entry, named by row id and resolved in the handler. The
/// resolution is the tenant boundary: another tenant's id is answered "not found".
/// </param>
/// <param name="EntryId">The agent's identifier for the entry to switch.</param>
/// <param name="Enabled">True installs it as a live crontab line; false comments it out.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record SetCronEntryEnabledCommand(
    Guid AccountId,
    string EntryId,
    bool Enabled,
    string IpAddress,
    string UserAgent);
