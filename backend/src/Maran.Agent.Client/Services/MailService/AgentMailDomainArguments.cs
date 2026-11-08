using Maran.SharedKernel.Security;

namespace Maran.Agent.Client.Services.MailService;

/// <summary>Everything one configuring of a mail domain carries.</summary>
/// <remarks>
/// The private key is held in a non-printing wrapper for the reason <c>CreateFtpsUserArguments</c> gives
/// for its password: a record's generated <c>ToString()</c> prints every property, and the defence is a
/// property of the type, not of every call site that might interpolate it.
/// </remarks>
/// <param name="AccountUsername">System username of the hosting account that owns the domain.</param>
/// <param name="Domain">The mail domain; a name of at least two labels.</param>
/// <param name="DkimSelector">The selector the active DKIM key is published under.</param>
/// <param name="DkimPrivateKeyPem">
/// The active DKIM private key as PKCS#8 PEM, RSA. Unwrapped exactly once, onto the wire, and handed to
/// the error translator so the agent quoting it back cannot put it in the panel's log.
/// </param>
public sealed record AgentMailDomainArguments(
    string AccountUsername,
    string Domain,
    string DkimSelector,
    SensitiveString DkimPrivateKeyPem);
