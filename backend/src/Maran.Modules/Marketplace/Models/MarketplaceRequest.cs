using Maran.Modules.Marketplace.Domain.Enums;

namespace Maran.Modules.Marketplace.Models;

/// <summary>One request to the marketplace host, described without any socket detail.</summary>
/// <param name="Method">GET or POST. Only a GET is ever retried or redirected.</param>
/// <param name="PathAndQuery">The path and query relative to the configured host, starting with a slash.</param>
/// <param name="Locale">The language the service localises its display text in.</param>
/// <param name="Lease">Whether the lease credential is required, optional or never attached.</param>
/// <param name="Limit">Which response-size cap applies to the answer.</param>
/// <param name="Body">The JSON body bytes, or null for a request without one.</param>
/// <param name="IfNoneMatch">The cached answer's ETag for a conditional GET, or null.</param>
public sealed record MarketplaceRequest(
    HttpMethod Method,
    string PathAndQuery,
    string Locale,
    LeaseRequirement Lease,
    ResponseLimit Limit,
    byte[]? Body = null,
    string? IfNoneMatch = null);
