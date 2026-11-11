namespace Maran.Modules.Mail.Jobs;

/// <summary>
/// The scheduled trigger for one pass that brings the host's local submission bindings in line with the
/// accounts that exist (issue #518). Carries no parameters: the pass reads the ledger and the accounts.
/// </summary>
/// <remarks>
/// A message rather than a timer callback, for the reason <c>CertificateRenewalRequested</c> gives: the bus is
/// durable and a failed pass is visible with every other failed message. The Host owns the cadence.
/// </remarks>
public sealed record MailSubmissionReconcileRequested;
