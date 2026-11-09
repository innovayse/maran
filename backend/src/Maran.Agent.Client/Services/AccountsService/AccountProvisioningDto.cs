namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>
/// The per-account options the agent applies when it creates the system user, beyond the name and the
/// disk quota.
/// </summary>
/// <param name="InodeLimit">
/// The ceiling on files and directories the account may own, or <c>null</c> for no ceiling. A figure is
/// never zero and never above what the wire carries (<see cref="uint.MaxValue"/>).
/// </param>
/// <param name="Shell">The login shell to give the system user.</param>
public sealed record AccountProvisioningDto(ulong? InodeLimit, AccountLoginShell Shell);
