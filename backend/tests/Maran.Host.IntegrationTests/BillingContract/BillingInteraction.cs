namespace Maran.Host.IntegrationTests.BillingContract;

/// <summary>One step of the billing contract: a request, and what the provider relies on in the answer.</summary>
/// <param name="Name">What the step does, in words; unique in the file.</param>
/// <param name="Request">The request the provider sends.</param>
/// <param name="Response">What the provider relies on in the answer.</param>
/// <param name="Note">A sentence for the reader of the file, or <c>null</c>; the replay ignores it.</param>
/// <param name="Credential">Which key signs the call, or <c>null</c> for <c>full</c>.</param>
/// <param name="Requires">Inputs the step needs; the step is skipped when the replay lacks one, or <c>null</c> for none.</param>
/// <param name="Arrange">The condition on the host the step needs, or <c>null</c> for none.</param>
public sealed record BillingInteraction(
    string Name,
    BillingRequest Request,
    BillingResponse Response,
    string? Note = null,
    string? Credential = null,
    IReadOnlyList<string>? Requires = null,
    string? Arrange = null);
