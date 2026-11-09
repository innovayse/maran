using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Mail.Commands.CreateMailDomain;

/// <summary>
/// Makes an account's site domain a mail domain: the host starts accepting mail for it and a DKIM
/// signing key is generated for it.
/// </summary>
/// <remarks>
/// The domain must be the primary domain of a site the SAME account owns. That is the whole proof of
/// ownership (no outbound DNS lookup is made), and it is why the command names an account at all:
/// the answer to "may this account receive mail for this name" is a fact about the account's sites.
/// </remarks>
/// <param name="AccountId">The account that will own the mail domain.</param>
/// <param name="Name">The domain, as the customer typed it; compared and stored lower-case.</param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record CreateMailDomainCommand(
    Guid AccountId,
    string Name,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
