namespace Maran.Modules.Sftp.Tests.TestSupport;

/// <summary>One delete the agent double was asked for, exactly as the handler addressed it.</summary>
/// <param name="AccountUsername">The account the call was addressed to.</param>
/// <param name="SftpUsername">The login name SUFFIX, which the handler must take from the row rather than derive.</param>
public sealed record AgentDeleteCall(string AccountUsername, string SftpUsername);
