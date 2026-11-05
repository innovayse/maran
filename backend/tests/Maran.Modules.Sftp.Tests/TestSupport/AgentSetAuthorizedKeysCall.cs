namespace Maran.Modules.Sftp.Tests.TestSupport;

/// <summary>One key-set replacement the agent double was asked for, exactly as the handler addressed it.</summary>
/// <param name="AccountUsername">The account the call was addressed to.</param>
/// <param name="SftpUsername">The login name SUFFIX, which the handler must take from the row rather than derive.</param>
/// <param name="PublicKeys">The complete key set that was sent; empty means "clear every key".</param>
public sealed record AgentSetAuthorizedKeysCall(
    string AccountUsername,
    string SftpUsername,
    IReadOnlyList<string> PublicKeys);
