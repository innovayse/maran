namespace Maran.Host.IntegrationTests.BillingContract;

/// <summary>Everything a replay of the billing contract is given: the client, and the inputs it can offer.</summary>
/// <param name="Client">The client the requests are sent through.</param>
/// <param name="Inputs">The inputs the replay supplies, name to value.</param>
/// <param name="Credentials">The keys the replay holds, credential name to the key's plaintext.</param>
/// <param name="Arrangements">The host conditions the replay can create.</param>
public sealed record BillingReplayEnvironment(
    HttpClient Client,
    IReadOnlyDictionary<string, string> Inputs,
    IReadOnlyDictionary<string, string> Credentials,
    IReadOnlyList<BillingArrangement> Arrangements);
