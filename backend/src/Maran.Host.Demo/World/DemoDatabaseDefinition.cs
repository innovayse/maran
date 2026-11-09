namespace Maran.Host.Demo.World;

/// <summary>One database of the demo world.</summary>
/// <param name="Suffix">The database name after the account prefix.</param>
/// <param name="UserSuffix">The database user's name after the account prefix.</param>
public sealed record DemoDatabaseDefinition(string Suffix, string UserSuffix);
