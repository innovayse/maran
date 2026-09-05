namespace Maran.Modules.Cron.Controllers.Requests;

/// <summary>The body of <c>POST /api/v1/cron-entries/{entryId}/enabled</c>.</summary>
/// <remarks>
/// The flag is sent explicitly rather than the route offering a "toggle": a toggle applied to a
/// state the caller last saw some seconds ago switches whatever it finds, so two clicks that race —
/// or one click on a stale screen — leave the entry in the state nobody chose.
/// </remarks>
/// <param name="AccountId">The account whose crontab holds the entry.</param>
/// <param name="Enabled">True installs it as a live crontab line; false comments it out.</param>
public sealed record SetCronEntryEnabledRequest(Guid AccountId, bool Enabled);
