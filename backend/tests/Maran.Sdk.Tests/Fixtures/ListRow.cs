namespace Maran.Sdk.Tests.Fixtures;

/// <summary>The row a <see cref="ListRequestValidator"/>'s sort map orders.</summary>
/// <param name="Id">Unique id; the tie-breaker.</param>
/// <param name="Name">The one sortable field.</param>
public sealed record ListRow(int Id, string Name);
