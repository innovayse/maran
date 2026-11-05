namespace Maran.Sdk.Contracts;

/// <summary>
/// The key was used before, by an identical request, and finished: the stored answer is to be sent
/// again instead of running the action a second time.
/// </summary>
/// <param name="StatusCode">The stored HTTP status.</param>
/// <param name="ContentType">The stored content type, or null when the answer had no body or none was stored.</param>
/// <param name="Body">The stored serialized body, or null when the answer had no body or none was stored.</param>
public sealed record IdempotencyReplay(int StatusCode, string? ContentType, string? Body) : IdempotencyBegin;
