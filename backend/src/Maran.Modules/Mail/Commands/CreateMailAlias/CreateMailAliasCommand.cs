using System.Text.Json.Serialization;
using Maran.Modules.Mail.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Mail.Commands.CreateMailAlias;

/// <summary>
/// Creates an alias, a forwarder or the catch-all of one mail domain.
/// </summary>
/// <remarks>
/// The account is not in the command: it is the owner of the domain, read from the domain row through
/// the caller's tenant scope, so no request can name one account's domain and another account's name.
/// Whether the address is an alias or a forwarder is DERIVED from where it goes, and the kind stated here
/// must agree with that derivation rather than override it.
/// </remarks>
/// <param name="MailDomainId">The domain the address lives in.</param>
/// <param name="Kind">What the caller says the address is; it must match where the targets go.</param>
/// <param name="LocalPart">The part before the <c>@</c>; empty for a catch-all, which has none.</param>
/// <param name="Targets">Where the mail goes: at least one and at most the module's ceiling, no repeats.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Established by the server.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Established by the server.</param>
public sealed record CreateMailAliasCommand(
    Guid MailDomainId,
    MailAliasKind Kind,
    string LocalPart,
    IReadOnlyList<string> Targets,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
