using Maran.SharedKernel.Security;

namespace Maran.Modules.Databases.Tests.TestSupport;

/// <summary>One creation the agent double was asked for, exactly as the handler addressed it.</summary>
/// <param name="AccountUsername">The account the call was addressed to.</param>
/// <param name="DatabaseName">The database name SUFFIX; a fully-qualified name here would be a defect.</param>
/// <param name="DbUsername">The user name SUFFIX, likewise.</param>
/// <param name="Password">The minted password the handler sent.</param>
public sealed record AgentCreateCall(
    string AccountUsername,
    string DatabaseName,
    string DbUsername,
    SensitiveString Password);
