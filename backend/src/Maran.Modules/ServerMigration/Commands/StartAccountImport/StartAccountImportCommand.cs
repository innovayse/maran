using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.StartAccountImport;

/// <summary>
/// Starts the job that creates the account of a staged import (issue #127). The administrator confirms twice,
/// by typing the account name and the key fingerprint they compared.
/// </summary>
/// <param name="PlanId">The plan the account is created against on this server.</param>
/// <param name="ResellerId">The reseller that is to own the account, or <c>null</c> for an account of the administrator's own.</param>
/// <param name="ConfirmAccountName">The account's name, typed; compared ordinally and case-sensitively.</param>
/// <param name="ConfirmKeyFingerprint">The sending server's key fingerprint, typed; compared ordinally and case-sensitively.</param>
/// <param name="ImportId">The import, taken from the route and never from a body.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal; read from the connection, never bound.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal; read from the request, never bound.</param>
public sealed record StartAccountImportCommand(
    Guid PlanId,
    Guid? ResellerId,
    string ConfirmAccountName,
    string ConfirmKeyFingerprint,
    [property: JsonIgnore][property: BindNever][BindNever] Guid ImportId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
