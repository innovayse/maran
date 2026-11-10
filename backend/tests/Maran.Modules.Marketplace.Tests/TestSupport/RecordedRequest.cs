namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>What the scripted handler saw of one request, copied before the request is disposed.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Uri">The full address.</param>
/// <param name="Headers">Every request header, name to joined values.</param>
/// <param name="Body">The body text, or null when the request had none.</param>
public sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    IReadOnlyDictionary<string, string> Headers,
    string? Body);
