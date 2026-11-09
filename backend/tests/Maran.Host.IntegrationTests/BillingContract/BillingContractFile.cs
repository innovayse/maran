namespace Maran.Host.IntegrationTests.BillingContract;

/// <summary>The billing contract as <c>docs/api/billing-contract/lifecycle.json</c> states it.</summary>
/// <param name="Contract">The contract's name.</param>
/// <param name="Revision">The revision of the file; a breaking edit raises it.</param>
/// <param name="BasePath">The prefix every step's path is relative to.</param>
/// <param name="Inputs">The inputs a replay may supply, name to meaning.</param>
/// <param name="Credentials">The credentials a replay may supply, name to meaning.</param>
/// <param name="Arrangements">The host conditions a replay may create, name to meaning.</param>
/// <param name="Builtins">The values every replay makes up for itself, name to meaning.</param>
/// <param name="Interactions">The steps, in the order they are replayed.</param>
public sealed record BillingContractFile(
    string Contract,
    int Revision,
    string BasePath,
    IReadOnlyDictionary<string, string> Inputs,
    IReadOnlyDictionary<string, string> Credentials,
    IReadOnlyDictionary<string, string> Arrangements,
    IReadOnlyDictionary<string, string> Builtins,
    IReadOnlyList<BillingInteraction> Interactions);
