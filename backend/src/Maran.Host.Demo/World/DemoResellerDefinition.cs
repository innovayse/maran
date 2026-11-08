using Maran.Modules.Accounts.Common;

namespace Maran.Host.Demo.World;

/// <summary>A reseller of the demo world, with the plans it owns.</summary>
/// <param name="Name">The reseller's name; also its login name.</param>
/// <param name="ContactEmail">The reseller's contact address.</param>
/// <param name="Envelope">What the reseller may allocate.</param>
/// <param name="Plans">The plans the reseller creates for its own accounts.</param>
public sealed record DemoResellerDefinition(
    string Name,
    string ContactEmail,
    ResellerEnvelopeDto Envelope,
    IReadOnlyList<DemoPlanDefinition> Plans);
