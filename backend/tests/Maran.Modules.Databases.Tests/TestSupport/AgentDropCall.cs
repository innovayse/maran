namespace Maran.Modules.Databases.Tests.TestSupport;

/// <summary>One drop the agent double was asked for, exactly as the handler addressed it.</summary>
/// <param name="AccountUsername">The account the call was addressed to.</param>
/// <param name="DatabaseName">The database name SUFFIX.</param>
/// <param name="DbUsername">The user name SUFFIX, which the handler must take from the row rather than derive.</param>
public sealed record AgentDropCall(string AccountUsername, string DatabaseName, string DbUsername);
