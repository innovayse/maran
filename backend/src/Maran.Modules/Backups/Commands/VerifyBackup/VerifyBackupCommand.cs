using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Backups.Commands.VerifyBackup;

/// <summary>
/// Asks the agent to read one stored backup artifact back and records what it found (issue #134).
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries an id and nothing else the caller could aim.</b> No account name, no path, no
/// destination: the account and the destination are read from the backup's own row, so a request that
/// could name them would be a request that points the panel's root agent at an artifact of the
/// caller's choosing.
/// </para>
/// <para>
/// There is no body. The action builds the command from the route id and the connection, and the
/// guard attributes stay on every member regardless — they are a property of the type, so a command
/// that is bound from a body later is already safe (rules/csharp.md "Server-established members").
/// </para>
/// </remarks>
/// <param name="BackupId">
/// The backup to verify. Another account's answers not-found. It comes from the ROUTE and is stamped by
/// the action.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record VerifyBackupCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid BackupId,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
