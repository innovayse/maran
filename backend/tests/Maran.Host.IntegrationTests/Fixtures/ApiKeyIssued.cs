namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>A key as its creator saw it: the identity the list shows and the secret shown once.</summary>
/// <param name="Id">The key's id.</param>
/// <param name="Prefix">The public prefix the list shows.</param>
/// <param name="Secret">The plaintext key, which only the create response ever carries.</param>
public sealed record ApiKeyIssued(Guid Id, string Prefix, string Secret);
