using Maran.Modules.Marketplace.Interfaces;

namespace Maran.Modules.Marketplace.Models;

/// <summary>The service's answer to "which of these installed modules have updates or revocations".</summary>
/// <param name="Items">One notice per installed module the service has something to say about.</param>
/// <param name="SignedAt">When the service signed the report.</param>
/// <param name="ValidUntil">The last moment the service stands behind the report.</param>
public sealed record UpdatesReport(
    IReadOnlyList<ModuleUpdateNotice> Items,
    DateTimeOffset SignedAt,
    DateTimeOffset ValidUntil) : ISignedMarketplaceDocument;
