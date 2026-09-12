using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Ftp.Commands.CreateFtpUser;

/// <summary>
/// Creates an FTPS login for an account — a system account jailed into that account's own FTPS jail
/// — and mints the password it is created with (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// The command carries no password, and none may be added: the panel generates one
/// (<c>ProvisionedPasswordGenerator</c>) rather than accepting one, so there is no customer-chosen
/// value to validate, to transport, or to find in a request log.
/// </para>
/// <para>
/// It carries no jail path either, and there is none to carry. The agent derives the jail from the
/// validated account name and creates it root-owned — so the customer names no directory, nothing
/// here has a path to validate, and the chroot-escape class of bug has nothing to aim at.
/// <c>ftp.proto</c> reserves the field name for exactly this reason.
/// </para>
/// </remarks>
/// <param name="AccountId">The account that will own the login.</param>
/// <param name="Name">The login name the customer asked for, without the account prefix.</param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record CreateFtpUserCommand(
    Guid AccountId,
    string Name,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
