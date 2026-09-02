using Maran.SharedKernel.Security;

namespace Maran.Modules.Sftp.Tests.TestSupport;

/// <summary>One creation the agent double was asked for, exactly as the handler addressed it.</summary>
/// <param name="AccountUsername">The account the call was addressed to.</param>
/// <param name="SftpUsername">The login name SUFFIX; a fully-qualified name here would be a defect.</param>
/// <param name="Password">The minted password the handler sent.</param>
public sealed record AgentCreateCall(string AccountUsername, string SftpUsername, SensitiveString Password);
