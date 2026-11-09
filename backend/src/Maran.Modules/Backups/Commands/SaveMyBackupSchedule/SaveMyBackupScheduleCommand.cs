using System.Text.Json.Serialization;
using Maran.Modules.Backups.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Backups.Commands.SaveMyBackupSchedule;

/// <summary>
/// Creates or replaces the signed-in customer's own backup schedule (issue #147).
/// </summary>
/// <remarks>
/// <para>
/// <b>It names no account and no destination, on purpose.</b> The account is the caller's own, written
/// by the action from <c>CurrentUser.Caller</c> and never read from the request: an account id in a
/// body is a tenant boundary crossed in a JSON field. A destination is an identifier of a row an
/// administrator configured, and a customer aiming the root agent at one they chose is exactly what
/// <c>CreateBackupCommand</c> already refuses to carry; the row therefore keeps the destination it has,
/// or the default for a schedule that is new.
/// </para>
/// <para>
/// There is no way to name the host-wide policy from here: the account member is a <see cref="Guid"/>,
/// not a nullable one, and the handler refuses an account that is not the caller's.
/// </para>
/// </remarks>
/// <param name="Frequency">How often the backup is taken; see <c>CustomerBackupLimits</c>.</param>
/// <param name="HourUtc">The hour of the day, in UTC, at which it is taken.</param>
/// <param name="DayOfWeekUtc">The weekday for a weekly schedule; must be absent for a daily one.</param>
/// <param name="RetainCount">How many successful backups to keep, up to the customer ceiling.</param>
/// <param name="Enabled">Whether the schedule runs at all.</param>
/// <param name="AccountId">
/// The caller's own account. Established by the server from the signed-in caller and stamped by the
/// action; never bound from the request.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record SaveMyBackupScheduleCommand(
    BackupFrequency Frequency,
    int HourUtc,
    DayOfWeek? DayOfWeekUtc,
    int RetainCount,
    bool Enabled,
    [property: JsonIgnore][property: BindNever][BindNever] Guid AccountId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
