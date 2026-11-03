namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The panel the view-as edge tests start from: two resellers, each owning one account, and one account
/// the administrator owns, every account with a customer login that owns it.
/// </summary>
/// <param name="Nova">The reseller <c>nova</c>, which owns <c>acme</c> (owner login <c>olive</c>).</param>
/// <param name="Orbit">The reseller <c>orbit</c>, which owns <c>globex</c> (owner login <c>gus</c>).</param>
/// <param name="Acme">The id of <c>acme</c>, owned by <see cref="Nova"/>.</param>
/// <param name="Globex">The id of <c>globex</c>, owned by <see cref="Orbit"/>.</param>
/// <param name="Initech">The id of <c>initech</c>, owned by the administrator (owner login <c>ivan</c>).</param>
public sealed record ViewAsWorld(SeededReseller Nova, SeededReseller Orbit, Guid Acme, Guid Globex, Guid Initech);
