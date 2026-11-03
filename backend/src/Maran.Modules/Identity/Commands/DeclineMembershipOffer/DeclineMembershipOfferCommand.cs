using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.DeclineMembershipOffer;

/// <summary>Declines an offer to join a hosting account's team, as the login the offer was made to.</summary>
/// <param name="OfferId">The offer, from the route. Never bound from a request body.</param>
/// <param name="IpAddress">The caller's address, established by the server and stamped by the action.
/// Never bound from a request body.</param>
/// <param name="UserAgent">The caller's user agent, established by the server and stamped by the action.
/// Never bound from a request body.</param>
public sealed record DeclineMembershipOfferCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid OfferId,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
