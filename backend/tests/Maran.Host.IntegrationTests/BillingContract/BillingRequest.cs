using System.Text.Json;

namespace Maran.Host.IntegrationTests.BillingContract;

/// <summary>The request a billing provider sends in one step of the contract.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The route relative to the contract's base path, placeholders allowed.</param>
/// <param name="Headers">The headers to send beside the credential, or <c>null</c> for none.</param>
/// <param name="Body">The JSON body, placeholders allowed inside its strings, or <c>null</c> for none.</param>
public sealed record BillingRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string>? Headers = null,
    JsonElement? Body = null);
