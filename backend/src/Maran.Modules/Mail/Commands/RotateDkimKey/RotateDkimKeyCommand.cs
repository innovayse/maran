using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Mail.Commands.RotateDkimKey;

/// <summary>
/// Replaces a mail domain's DKIM signing key with a new one. The previous key stays published as
/// retiring so mail signed with it still verifies while resolvers catch up with the new record.
/// </summary>
/// <param name="MailDomainId">The domain whose key is rotated; comes from the route.</param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record RotateDkimKeyCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid MailDomainId,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
