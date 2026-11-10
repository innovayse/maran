namespace Maran.Modules.Ssl.Services;

/// <summary>The fixed words of the ACME protocol (RFC 8555) the steps of an order share.</summary>
internal static class AcmeProtocol
{
    /// <summary>The empty JWS payload that turns a POST into a read (RFC 8555 §6.3).</summary>
    public const string PostAsGet = "";

    /// <summary>The order status meaning "every identifier is validated; send the CSR" (RFC 8555 §7.1.6).</summary>
    public const string OrderReady = "ready";
}
