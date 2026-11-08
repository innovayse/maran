namespace Maran.Sdk.Contracts;

/// <summary>
/// Published by the operator-alert dispatcher for every alert, as the event an alert webhook sender
/// subscribes to. No handler exists yet (the webhook itself is a later issue), so today publishing it
/// reaches nobody; the dispatcher publishes it already so that the sender is one new handler and no
/// change in any module that alerts. Carries no secret and no address, so it may travel any queue.
/// </summary>
/// <param name="Alert">What happened.</param>
/// <param name="OccurredAt">When the dispatcher handled it, from the panel's clock.</param>
public sealed record OperatorAlertRaised(OperatorAlert Alert, DateTimeOffset OccurredAt);
