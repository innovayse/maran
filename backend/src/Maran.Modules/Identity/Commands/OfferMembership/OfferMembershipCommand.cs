using System.Text.Json.Serialization;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.OfferMembership;

/// <summary>
/// Offers an existing login a place on the caller's account, to be accepted by that login inside the
/// panel. Nothing is granted until they accept.
/// </summary>
/// <param name="Identifier">The login name or the address of the person, typed once.</param>
/// <param name="Features">
/// The single features the person will hold if they accept. May be empty; each must be one real
/// feature, never a combined set.
/// </param>
/// <param name="IpAddress">The owner's address, established by the server and stamped by the action.
/// Never bound from a request body.</param>
/// <param name="UserAgent">The owner's user agent, established by the server and stamped by the action.
/// Never bound from a request body.</param>
public sealed record OfferMembershipCommand(
    string Identifier,
    IReadOnlyList<AccountFeature> Features,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
