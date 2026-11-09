using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Sites.Queries.GetPhpSettings;

/// <summary>Reads the signed-in customer's PHP settings.</summary>
/// <param name="AccountId">
/// The account whose settings are read. Established by the server from the caller's own account and
/// stamped by the action; never bound from the request, so a caller cannot name an account of their own.
/// </param>
public sealed record GetPhpSettingsQuery(
    [property: JsonIgnore][property: BindNever][BindNever] Guid AccountId = default);
