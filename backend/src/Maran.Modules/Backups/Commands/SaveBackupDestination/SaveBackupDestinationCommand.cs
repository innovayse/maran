using System.Text.Json.Serialization;
using Maran.Modules.Backups.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Backups.Commands.SaveBackupDestination;

/// <summary>Records a new place this server keeps backups (spec §11, §209).</summary>
/// <remarks>
/// <para>
/// <b>The command carries a kind and a label, and no path.</b> A local destination's root is the
/// agent's own — it refuses to be told one — so a path in this request would be a field the panel
/// accepts, stores, displays and never honours. The one local root is set in <c>panel.env</c> and
/// reconciled onto the default destination at startup.
/// </para>
/// <para>
/// <b>Every outcome of this command is a refusal on this build, and that is deliberate.</b> An
/// operator asking for an S3 destination gets a named error a screen can branch on and a sentence in
/// their own language, rather than a route that does not exist; an operator asking for a second local
/// one is told that the agent writes to one root. The alternative — no endpoint — leaves the settings
/// screen with nothing to say and the panel with no record that the question was asked.
/// </para>
/// </remarks>
/// <param name="Name">The label an operator will read on a screen.</param>
/// <param name="Kind">Which kind of storage the destination names.</param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record SaveBackupDestinationCommand(
    string Name,
    BackupDestinationKind Kind,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
