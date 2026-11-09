using Maran.Agent.Client.Services.DbService;

namespace Maran.Modules.Databases.Tests.TestSupport;

/// <summary>One privilege change the agent double was asked for, exactly as the handler addressed it.</summary>
/// <param name="AccountUsername">The account the call was addressed to.</param>
/// <param name="DbUsername">The user name SUFFIX.</param>
/// <param name="DatabaseName">The database name SUFFIX.</param>
/// <param name="Privileges">The complete set the handler asked the server to hold.</param>
public sealed record AgentPrivilegesCall(
    string AccountUsername,
    string DbUsername,
    string DatabaseName,
    IReadOnlyCollection<DatabasePrivilegeKind> Privileges);
