namespace Maran.Sdk.Contracts;

/// <summary>
/// Why a <see cref="SendMailRequested"/> was published. The sending module reads it to decide whether
/// the outcome of the send is also the outcome of an operator alert's mail channel.
/// </summary>
/// <remarks>
/// A closed set, not a free string: a password-reset mail that fails must never turn the "last alert
/// delivery failed" state red, so the distinction has to be one the compiler holds.
/// </remarks>
public enum MailPurpose
{
    /// <summary>An ordinary message to a person: a password reset, an invitation, a test mail.</summary>
    Transactional = 0,

    /// <summary>An alert addressed to the server's operator, published by the operator-alert dispatcher.</summary>
    OperatorAlert = 1,
}
