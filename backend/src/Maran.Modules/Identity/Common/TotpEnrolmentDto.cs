namespace Maran.Modules.Identity.Common;

/// <summary>What the user needs to add the panel to their authenticator app.</summary>
/// <remarks>
/// Enrolment is deliberately two steps: this hands over a secret and enables nothing. Only after
/// the user proves they can produce a code from it does two-factor authentication turn on — so
/// someone who scans the QR into a dead app, or closes the page halfway, is not locked out of their
/// own panel by an enrolment they never completed.
/// </remarks>
/// <param name="Secret">The base32 shared secret, for typing in by hand.</param>
/// <param name="ProvisioningUri">The same secret as an <c>otpauth://</c> URI, for the QR code.</param>
public sealed record TotpEnrolmentDto(string Secret, string ProvisioningUri);
