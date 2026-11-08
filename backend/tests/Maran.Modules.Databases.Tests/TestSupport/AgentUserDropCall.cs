namespace Maran.Modules.Databases.Tests.TestSupport;

/// <summary>One user drop the agent double was asked for, exactly as the handler addressed it.</summary>
/// <param name="AccountUsername">The account the call was addressed to.</param>
/// <param name="DbUsername">The user name SUFFIX the row recorded.</param>
public sealed record AgentUserDropCall(string AccountUsername, string DbUsername);
