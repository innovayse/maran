using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Dns.Commands.CreateZone;

/// <summary>
/// Creates an empty DNS zone for an account and hands it to the name server.
/// </summary>
/// <remarks>
/// A customer may create a zone only for a host name one of their own sites already claims, so the registrar
/// delegation that makes a zone live cannot be pre-empted by whoever asked first. An administrator may also
/// create a zone no site claims. Both are refused when the name collides with another account's zone.
/// </remarks>
/// <param name="AccountId">The account that will own the zone.</param>
/// <param name="Name">The apex name, such as <c>example.com</c>.</param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the action;
/// never bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request.
/// </param>
public sealed record CreateZoneCommand(
    Guid AccountId,
    string Name,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
