namespace Maran.Modules.Ssl.Jobs;

/// <summary>
/// The scheduled trigger for one certificate renewal pass. Carries no parameters: the pass renews
/// whatever is due, and "what is due" is decided by <see cref="CertificateRenewalHandler.RenewalWindow"/>
/// and the injected clock, never by whoever scheduled it.
/// </summary>
/// <remarks>
/// A message rather than a timer callback, because the panel's message bus is durable: a pass that
/// was scheduled while the panel was restarting still runs, and a pass that fails is visible in the
/// same place as every other failed message. Adding a second scheduling mechanism — a hosted service
/// with its own timer — would be a second thing to reason about for no gain (rules/architecture.md).
/// </remarks>
public sealed record CertificateRenewalRequested;
