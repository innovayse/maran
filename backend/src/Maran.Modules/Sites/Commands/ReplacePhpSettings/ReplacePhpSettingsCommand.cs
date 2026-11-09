using System.Text.Json.Serialization;
using Maran.Modules.Sites.Common;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Sites.Commands.ReplacePhpSettings;

/// <summary>
/// Replaces the signed-in customer's PHP settings, for every pool their account has (issue #138).
/// </summary>
/// <remarks>
/// A full replacement, not a patch: a setting the request does not list is removed and goes back to
/// PHP's own default. The screen always holds the whole set, and a patch would need a reading of
/// "absent" that cannot be told from "reset". Repeating the same request changes nothing.
/// </remarks>
/// <param name="Settings">
/// The settings to keep, each a name and a value in the wire spelling (mebibytes for a size, digits for
/// seconds and counts, a zone name for a timezone). A request that leaves the list out is refused; an
/// empty list removes every setting.
/// </param>
/// <param name="AccountId">
/// The account whose settings are replaced. Established by the server from the caller's own account and
/// stamped by the action; never bound from the request, so a body cannot aim the change at another
/// customer.
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Never bound from the request.</param>
public sealed record ReplacePhpSettingsCommand(
    IReadOnlyList<PhpSettingInputDto> Settings,
    [property: JsonIgnore][property: BindNever][BindNever] Guid AccountId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
