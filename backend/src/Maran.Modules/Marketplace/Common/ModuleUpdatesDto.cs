namespace Maran.Modules.Marketplace.Common;

/// <summary>What <c>GET /api/v1/marketplace/updates</c> answers: the one "check for updates" call.</summary>
/// <param name="Items">One entry per installed module; empty when the answer could not be verified.</param>
/// <param name="Verified">Whether the answer's signature and freshness checked out. False is a 200, not an error.</param>
/// <param name="CheckedAt">When the service signed the answer, or null when it was not verified.</param>
/// <param name="UnverifiedMessage">Why it was not verified, in the caller's language; only when <paramref name="Verified"/> is false.</param>
public sealed record ModuleUpdatesDto(
    IReadOnlyList<ModuleUpdateDto> Items,
    bool Verified,
    DateTimeOffset? CheckedAt,
    string? UnverifiedMessage);
