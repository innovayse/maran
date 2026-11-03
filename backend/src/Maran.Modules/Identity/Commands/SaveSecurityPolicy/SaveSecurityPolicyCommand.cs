using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.SaveSecurityPolicy;

/// <summary>Replaces the panel's security policy with the values an administrator submitted.</summary>
/// <param name="MinimumPasswordLength">The shortest password the panel accepts.</param>
/// <param name="ForceTwoFactorForAdmins">Whether an administrator without a second factor is steered into enrolment.</param>
/// <param name="MaxFailedLoginAttempts">Consecutive failed sign-ins that lock an account.</param>
/// <param name="LockoutMinutes">How long a locked account stays locked, in minutes.</param>
/// <param name="ForceTwoFactorForResellers">
/// Whether a reseller without a second factor is steered into enrolment. A body that leaves it out
/// reads as <see cref="Domain.Entities.SecurityPolicy.DefaultForceTwoFactorForResellers"/>, not as false: a client
/// written before resellers existed saves the whole policy, and reading its silence as "off" would
/// quietly turn off the protection of every reseller login.
/// </param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request body (rules/csharp.md "Server-established
/// members of a command").</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound from the request body.</param>
public sealed record SaveSecurityPolicyCommand(
    int MinimumPasswordLength,
    bool ForceTwoFactorForAdmins,
    int MaxFailedLoginAttempts,
    int LockoutMinutes,
    bool ForceTwoFactorForResellers = Domain.Entities.SecurityPolicy.DefaultForceTwoFactorForResellers,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
