using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Backups.Commands.SetBackupDestinationEncryption;

/// <summary>
/// Switches encryption of NEW backups on or off for one backup destination (issue #160).
/// </summary>
/// <remarks>
/// <para>
/// <b>It changes what the next backup does and nothing about the ones already written.</b> Switching
/// it on seals every backup taken from then on; switching it off stops that, and the key stays so the
/// sealed backups remain restorable (<c>BackupDestination.EncryptArchives</c>). The first time it is
/// switched on the panel mints the destination's key; no caller supplies or reads one — the key is
/// never on the wire in either direction.
/// </para>
/// <para>
/// Administrator-only, like every setting of a destination: where the server keeps archives and how
/// they are protected is a decision about the machine, not about a customer's account.
/// </para>
/// </remarks>
/// <param name="DestinationId">
/// The destination, from the route. Established by the action and never bound from the body, so a
/// request cannot name one destination in the URL and another in the document.
/// </param>
/// <param name="Enabled">Whether new backups written to the destination are sealed.</param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record SetBackupDestinationEncryptionCommand(
    bool Enabled,
    [property: JsonIgnore][property: BindNever][BindNever] Guid DestinationId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
