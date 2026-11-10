namespace Maran.Modules.Marketplace.Models;

/// <summary>An answer from the marketplace host: its status, its capped body and the two headers the panel reads.</summary>
/// <param name="StatusCode">The HTTP status.</param>
/// <param name="Body">The exact response bytes, which are what the detached signature covers.</param>
/// <param name="Signature">The <c>X-Maran-Signature</c> header value (base64 DER), or null when absent.</param>
/// <param name="ETag">The <c>ETag</c> header value, or null when absent.</param>
public sealed record MarketplaceResponse(int StatusCode, byte[] Body, string? Signature, string? ETag);
