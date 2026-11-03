using System.Text.Json.Serialization;
using Maran.Modules.Accounts.Common;
using Maran.SharedKernel.Constants;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Accounts.Commands.CreateReseller;

/// <summary>
/// Creates a reseller: the row that records what it was sold, and, through an event, the login it signs
/// in with. Bound directly by <c>POST /api/v1/resellers</c>, which only an administrator may call.
/// </summary>
/// <param name="Name">
/// The reseller's unique short name, which is also its login name, in the account name alphabet and
/// not a reserved system name.
/// </param>
/// <param name="ContactEmail">
/// The address the reseller's invitation is sent to, and its login's password-reset mails after it.
/// </param>
/// <param name="Language">
/// The contact's language, one of <see cref="PanelLanguages.Supported"/>; the invitation is written in
/// it, not in the language of the administrator creating the reseller. English when left out.
/// </param>
/// <param name="Envelope">What the reseller is sold: its account and plan counts, disk pool and ceilings.</param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Read from the connection by the controller and
/// never bound from the request, by the attribute pair the account commands carry for the same reason.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Server-established and guarded exactly as
/// <paramref name="IpAddress"/> is.
/// </param>
public sealed record CreateResellerCommand(
    string Name,
    string ContactEmail,
    ResellerEnvelopeDto Envelope,
    string Language = PanelLanguages.English,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
