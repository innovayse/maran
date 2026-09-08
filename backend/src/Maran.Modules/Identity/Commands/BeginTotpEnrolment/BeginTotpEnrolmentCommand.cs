using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.BeginTotpEnrolment;

/// <summary>Starts a two-factor enrolment, without enabling anything.</summary>
/// <param name="UserId">The caller, established by the server from their own access token and
/// stamped by the action. Never bound from the request body — a bound user id would let any
/// authenticated caller aim this command at somebody else's account.</param>
public sealed record BeginTotpEnrolmentCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid UserId);
