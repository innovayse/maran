using Maran.SharedKernel.Security;

namespace Maran.Modules.Databases.Tests.TestSupport;

/// <summary>One password change the agent double was asked for.</summary>
/// <param name="AccountUsername">The account the call was addressed to.</param>
/// <param name="DbUsername">The user name SUFFIX the row recorded.</param>
/// <param name="Password">The replacement the handler minted.</param>
public sealed record AgentSetPasswordCall(string AccountUsername, string DbUsername, SensitiveString Password);
