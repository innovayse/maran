namespace Maran.Modules.Backups.Common;

/// <summary>
/// The answer to "what is scheduled for this scope": the host-wide policy, or one account's
/// override.
/// </summary>
/// <remarks>
/// <para>
/// <b>A scope with nothing recorded is an answer, not a failure.</b> A fresh server has no backup
/// schedule until an administrator saves one, so "nothing is scheduled here" is the ordinary first
/// state of the settings screen. It used to be a <c>404 BackupScheduleNotFound</c>, which every
/// browser logs as a failed request and which the SPA then had to recognise by code and re-read as
/// success. The read now always succeeds and this shape carries the answer.
/// </para>
/// <para>
/// The panel still invents no default: <see cref="Schedule"/> is absent rather than a made-up "daily
/// at 03:00, disabled", which an operator would take for what the server holds.
/// </para>
/// </remarks>
/// <param name="Schedule">
/// The schedule recorded for the scope, or <c>null</c> when none has ever been saved for it. That is
/// the field's only meaning: a recorded schedule that is switched off is present, with
/// <see cref="BackupScheduleDto.Enabled"/> false.
/// </param>
public sealed record BackupScheduleScopeDto(BackupScheduleDto? Schedule);
