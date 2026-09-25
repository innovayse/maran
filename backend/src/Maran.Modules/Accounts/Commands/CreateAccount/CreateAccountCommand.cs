using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Accounts.Commands.CreateAccount;

/// <summary>
/// Creates a hosting account: its Linux user, home directory and disk quota through the agent,
/// and then the row that records it (spec §8 — an Account IS a system user, and the isolation
/// between customers is the operating system's).
/// </summary>
/// <remarks>
/// This command is bound directly by <c>AccountsController.CreateAsync</c>; there is no separate
/// request type. <paramref name="IpAddress"/> and <paramref name="UserAgent"/> are the two fields a
/// caller must not be able to state, and the pair of attributes on them is what makes that true —
/// see their own documentation.
/// </remarks>
/// <param name="Name">The account's unique, Linux-username-safe short name.</param>
/// <param name="PrimaryDomain">The account's primary domain.</param>
/// <param name="PlanId">The id of the plan bounding this account's resource limits.</param>
/// <param name="OwnerEmail">
/// The account's own contact address, stored on <c>Account.OwnerEmail</c> and carried to Identity
/// on <see cref="Maran.Sdk.Events.AccountCreated"/>, which stores it again on the login it creates.
/// The two copies start equal and are allowed to diverge afterwards — see <c>Account.OwnerEmail</c>'s
/// own remarks for why a second copy is the deliberate choice: it is what lets an administrator
/// resend an invitation for an account whose login was never successfully created.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Read from the connection by the controller
/// and never bound from the request: <c>JsonIgnore</c> keeps it out of the JSON body — a
/// <c>[FromBody]</c> parameter is handed to an input formatter, which the model-binding pipeline
/// never sees, so <c>BindNever</c> alone would leave it writable — and <c>BindNever</c> keeps it out
/// of the query and form pipelines, where <c>JsonIgnore</c> means nothing. The two cover disjoint
/// pipelines, so both are always present. The default exists so that <c>[ApiController]</c>'s
/// implicit-required validation does not reject every body for omitting a field the body is
/// forbidden to carry.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Server-established and guarded exactly as
/// <paramref name="IpAddress"/> is, for the same reason.
/// </param>
public sealed record CreateAccountCommand(
    string Name,
    string PrimaryDomain,
    Guid PlanId,
    string OwnerEmail,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
