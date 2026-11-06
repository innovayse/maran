namespace Maran.Sdk.Contracts;

/// <summary>The key is reserved by a request of the same caller that has not finished yet.</summary>
public sealed record IdempotencyInProgress : IdempotencyBegin;
