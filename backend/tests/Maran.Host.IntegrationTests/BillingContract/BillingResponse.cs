namespace Maran.Host.IntegrationTests.BillingContract;

/// <summary>What the billing provider relies on in the answer to one step: the status and what it reads.</summary>
/// <param name="Status">The HTTP status, always checked.</param>
/// <param name="Code">The machine code of the problem document, or <c>null</c> when the step expects a success.</param>
/// <param name="Headers">Response headers that must carry the stated value, or <c>null</c> for none.</param>
/// <param name="Empty">Whether the body must be empty, as the bodyless 401 is.</param>
/// <param name="Properties">The properties the provider reads, or <c>null</c> for none.</param>
/// <param name="Absent">Properties the answer must not carry, or <c>null</c> for none.</param>
/// <param name="Capture">Values kept for later steps, variable name to property path, or <c>null</c> for none.</param>
public sealed record BillingResponse(
    int Status,
    string? Code = null,
    IReadOnlyDictionary<string, string>? Headers = null,
    bool Empty = false,
    IReadOnlyList<ResponseExpectation>? Properties = null,
    IReadOnlyList<string>? Absent = null,
    IReadOnlyDictionary<string, string>? Capture = null);
