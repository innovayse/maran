using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Backups.Commands.RestoreBackup;

/// <summary>
/// Replaces an account from one of its backups: its home directory is swapped for the archive's and
/// each of its databases is dropped, re-created and reloaded (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the operation that can destroy a working account, and the command's shape says so.</b>
/// It is replace-within-scope, not undo: what the archive holds replaces what is there, and what the
/// archive does not hold is untouched — the vhosts, the certificates, the crontab and the firewall
/// rules stay exactly as they are. An account restored from a two-week-old backup is a two-week-old
/// home under today's site configuration.
/// </para>
/// <para>
/// <b><see cref="ConfirmAccountUsername"/> is the whole of the confirmation, and it is a field of the
/// command rather than a header or a query flag.</b> A boolean "yes I am sure" is satisfied by a
/// click, by a replayed request, and by a script that sets every flag it is offered; typing the
/// account's system user name is not, and the value being typed is the same value that names what is
/// about to be overwritten. It is compared against the TARGET account's name and never against the
/// caller's own, so an administrator restoring on a customer's behalf has to type the customer's
/// name — which is the case where a mis-clicked row does the most damage.
/// </para>
/// <para>
/// It carries no destination and no path, for the reason
/// <see cref="Commands.CreateBackup.CreateBackupCommand"/> carries none: where the bytes live is the
/// operator's configuration, and a request that could name a location would be a request that aims
/// the panel's root agent at a file of the caller's choosing.
/// </para>
/// <para>
/// It carries no list of databases either. Which databases a restore may replace is the PANEL'S
/// answer, read from its own rows, not the caller's — a caller-supplied list would let somebody
/// name a database the panel does not own and have the agent asked about it.
/// </para>
/// </remarks>
/// <param name="BackupId">
/// The backup to restore from. Another customer's answers not-found. It comes from the ROUTE and is
/// stamped by the action; a body-bound backup id would let a caller confirm one account's name and
/// have a different account's backup restored, which is the whole confirmation defeated in one field.
/// </param>
/// <param name="ConfirmAccountUsername">
/// The target account's system user name, typed by the caller. Anything but an exact match refuses
/// the restore before the agent is asked for anything. Optional on the wire so that omitting it is
/// answered by the validator's <c>RestoreConfirmationRequired</c> rather than by a bare framework
/// 400 that says nothing about what was missing.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record RestoreBackupCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid BackupId,
    string ConfirmAccountUsername = "",
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
