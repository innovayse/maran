using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Backups.Commands.CreateBackup;

/// <summary>
/// Takes a backup of one account now: an archive of its home directory plus a dump of each of its
/// databases, written to the panel's configured storage location (spec §11).
/// </summary>
/// <remarks>
/// The command names an account and nothing else. It carries no destination, no path and no
/// retention — where the bytes go is the operator's configuration, not a per-request choice, and a
/// request that could name a location would be a request that aims the panel's root agent at a
/// directory of the caller's choosing.
///
/// It carries no kind either: everything this command produces is a manual backup, because a
/// scheduled or pre-deletion copy is taken by the panel and never asked for over HTTP. A caller who
/// could name the kind could mark an ordinary backup as one retention must never prune.
/// </remarks>
/// <param name="AccountId">
/// The account to back up. Bound from the body and deliberately NOT guarded: this is an
/// administrator's panel, and an administrator taking a backup FOR a customer names that customer.
/// Which accounts a caller may name is an authorization question, answered by the module's policy
/// and by <c>BackupsDbContext</c>'s tenant query filter, never by model binding.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record CreateBackupCommand(
    Guid AccountId,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
