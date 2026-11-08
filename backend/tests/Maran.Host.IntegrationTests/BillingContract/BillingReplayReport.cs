namespace Maran.Host.IntegrationTests.BillingContract;

/// <summary>The verdict of one replay of the billing contract.</summary>
/// <param name="Replayed">How many steps were sent.</param>
/// <param name="Skipped">The steps that were not sent, each with the reason, which is never a pass.</param>
/// <param name="Failures">Every difference between the contract and the panel, each naming its step.</param>
public sealed record BillingReplayReport(
    int Replayed,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> Failures);
