namespace Maran.Host.Demo.World;

/// <summary>One whitelisted range of the demo world.</summary>
/// <param name="Cidr">The range.</param>
/// <param name="Note">The note shown beside it.</param>
public sealed record DemoWhitelistDefinition(string Cidr, string Note);
