using Maran.SharedKernel.Security;

namespace Maran.Modules.Databases.Tests.TestSupport;

/// <summary>One user creation the agent double was asked for.</summary>
/// <param name="AccountUsername">The account the call was addressed to.</param>
/// <param name="DbUsername">The user name SUFFIX.</param>
/// <param name="Password">The password the handler minted.</param>
public sealed record AgentUserCreateCall(string AccountUsername, string DbUsername, SensitiveString Password);
