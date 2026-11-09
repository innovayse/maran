namespace Maran.Host.IntegrationTests.BillingContract;

/// <summary>A condition on the host that a step of the contract needs, which the replay can create and undo.</summary>
/// <param name="Name">The arrangement's name as the contract states it.</param>
/// <param name="ApplyAsync">Makes the condition true, before the step is sent.</param>
/// <param name="RestoreAsync">Makes it false again, after the step has been answered.</param>
public sealed record BillingArrangement(string Name, Func<Task> ApplyAsync, Func<Task> RestoreAsync);
