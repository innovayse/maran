using System.Text.Json.Serialization;
using Maran.Modules.Backups.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Backups.Commands.SaveBackupSchedule;

/// <summary>
/// Creates or replaces one backup schedule: the host-wide policy, or one account's override
/// (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// One command for both create and replace, because the settings screen has one form and a schedule
/// has one identity per account — there is nothing an operator could mean by "add a second schedule
/// for this account" except changing the first.
/// </para>
/// <para>
/// <b>It names a destination and no kind.</b> A destination is an identifier of a row an
/// administrator already configured, never a path, so naming one cannot aim the panel's root agent
/// anywhere the operator has not already approved — which is the reason <c>CreateBackupCommand</c>
/// still refuses to carry one, since that surface is reachable by a customer. Everything a schedule
/// produces is <see cref="BackupKind.Scheduled"/>, because a caller who could name the kind could
/// mark an ordinary backup as one retention must never prune.
/// </para>
/// </remarks>
/// <param name="AccountId">
/// The account to back up, or <c>null</c> for every account on the host. Bound from the body and
/// deliberately NOT guarded: this is an administrator's setting and an administrator scheduling a
/// backup FOR a customer names that customer. Who may name an account at all is an authorization
/// question, answered by the controller's administrator policy, never by model binding.
/// </param>
/// <param name="DestinationId">
/// The destination the schedule writes to, or <c>null</c> for this server's default one. A missing
/// or unusable destination is refused when the run starts, not here, because a destination can be
/// removed between the form being saved and the night it fires.
/// </param>
/// <param name="Frequency">How often the backup is taken.</param>
/// <param name="HourUtc">The hour of the day, in UTC, at which it is taken.</param>
/// <param name="DayOfWeekUtc">The weekday for a weekly schedule; must be absent for a daily one.</param>
/// <param name="RetainCount">How many successful backups to keep before the oldest are pruned.</param>
/// <param name="Enabled">Whether the schedule runs at all.</param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record SaveBackupScheduleCommand(
    Guid? AccountId,
    Guid? DestinationId,
    BackupFrequency Frequency,
    int HourUtc,
    DayOfWeek? DayOfWeekUtc,
    int RetainCount,
    bool Enabled,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
