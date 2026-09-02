namespace Maran.Modules.Sftp.Commands.CreateSftpUser;

/// <summary>
/// Creates an SFTP login for an account — a system account jailed into that account's own chroot —
/// and mints the password it is created with (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// The command carries no password, and none may be added: the panel generates one
/// (<c>ProvisionedPasswordGenerator</c>) rather than accepting one, so there is no customer-chosen
/// value to validate, to transport, or to find in a request log.
/// </para>
/// <para>
/// It carries no chroot path either, and there is none to carry. OpenSSH confines every login here
/// with a fixed <c>ChrootDirectory %h</c>, and the agent derives that jail from the validated
/// account name — so the customer names no directory, nothing here has a path to validate, and the
/// chroot-escape class of bug has nothing to aim at.
/// </para>
/// </remarks>
/// <param name="AccountId">The account that will own the login.</param>
/// <param name="Name">The login name the customer asked for, without the account prefix.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record CreateSftpUserCommand(Guid AccountId, string Name, string IpAddress, string UserAgent);
