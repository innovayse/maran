using System.Text.RegularExpressions;
using FluentValidation;
using Maran.Modules.Ssl.Resources;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Ssl.Commands.InstallCustomCertificate;

/// <summary>
/// Validates <see cref="InstallCustomCertificateCommand"/> before it reaches the handler
/// (rules/security.md "Input").
/// </summary>
/// <remarks>
/// <para>
/// The domain is checked for shape AND for reservation: an address literal, a loopback or
/// special-use name (<see cref="ReservedHostNameRule"/>) and the panel's own name
/// (<see cref="IReservedHostDirectory"/>) are refused with a message of their own, exactly as
/// <c>IssueCertificate</c> does, so the reservation cannot be bypassed through this second door.
/// </para>
/// <para>
/// The certificate and key fields are checked for presence, size and PEM armour, and each failure
/// has a code of its own naming the field and the problem — "missing", "not PEM", "too large" — so
/// the screen can put the sentence under the text area it is about. Each rule stops at its first
/// failure: an empty field is reported as missing, never also as "not PEM".
/// </para>
/// <para>
/// Whether the PEM actually parses, and whether the key matches the certificate, is decided by the
/// agent, which has the material and openssl to check it. Its three verdicts come back as their own
/// codes (<c>AgentCertificateUnreadable</c>, <c>AgentPrivateKeyUnreadable</c>,
/// <c>AgentPrivateKeyMismatch</c>). The panel asserting a parse or a match it did not perform with
/// the tool that will serve the file would be worse than not checking, because a disagreement would
/// then surface as nginx refusing to start.
/// </para>
/// </remarks>
public sealed partial class InstallCustomCertificateCommandValidator
    : AbstractValidator<InstallCustomCertificateCommand>
{
    /// <summary>The armour a PEM certificate block must begin with.</summary>
    private const string CertificatePemPrefix = "-----BEGIN CERTIFICATE-----";

    /// <summary>The largest chain or key the panel accepts, in characters.</summary>
    private const int MaximumMaterialLength = 65536;

    /// <summary>Configures the field rules for <see cref="InstallCustomCertificateCommand"/>.</summary>
    /// <param name="panelHosts">Answers whether a name is the panel's own.</param>
    public InstallCustomCertificateCommandValidator(IReservedHostDirectory panelHosts)
    {
        RuleFor(command => command.Domain)
            .NotEmpty()
            .MaximumLength(HostNameRule.MaximumLength)
            .Must(HostNameRule.IsHostName)
            .WithMessage(nameof(ErrorMessages.CertificateDomainInvalidFormat))
            .Must(domain =>
            {
                return !HostNameRule.IsHostName(domain)
                    || !(ReservedHostNameRule.IsReserved(domain) || panelHosts.IsPanelHostName(domain));
            })
            .WithMessage(nameof(ErrorMessages.CertificateDomainReserved));

        RuleFor(command => command.CertificatePem)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(nameof(ErrorMessages.CertificatePemRequired))
            .MaximumLength(MaximumMaterialLength)
            .WithMessage(nameof(ErrorMessages.CertificatePemTooLarge))
            .Must(IsArmouredCertificate)
            .WithMessage(nameof(ErrorMessages.CertificatePemInvalid));

        // The armour check accepts every private key label rather than pinning one: the label names
        // the key's algorithm — RSA, EC, or the generic PKCS#8 form — and pinning one here would
        // reject a perfectly good key for having been exported by a different tool. What it refuses
        // is text that is not a PEM private key at all, such as a second copy of the certificate.
        RuleFor(command => command.PrivateKeyPem)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(nameof(ErrorMessages.PrivateKeyPemRequired))
            .MaximumLength(MaximumMaterialLength)
            .WithMessage(nameof(ErrorMessages.PrivateKeyPemTooLarge))
            .Must(IsArmouredPrivateKey)
            .WithMessage(nameof(ErrorMessages.PrivateKeyPemInvalid));
    }

    /// <summary>Whether the supplied text at least carries a PEM certificate block.</summary>
    /// <param name="certificatePem">The submitted certificate text.</param>
    /// <returns><c>true</c> when the armour line is present.</returns>
    private static bool IsArmouredCertificate(string certificatePem)
    {
        return certificatePem.Contains(CertificatePemPrefix, StringComparison.Ordinal);
    }

    /// <summary>Whether the supplied text at least carries a PEM private key block, of any algorithm.</summary>
    /// <param name="privateKeyPem">The submitted key text.</param>
    /// <returns><c>true</c> when a BEGIN line labelled as a private key is present anywhere in it.</returns>
    /// <remarks>
    /// Anywhere, not first: <c>openssl ecparam -genkey</c> writes an <c>EC PARAMETERS</c> block ahead
    /// of the key, and that output is a key file people paste as it is.
    /// </remarks>
    private static bool IsArmouredPrivateKey(string privateKeyPem)
    {
        return PrivateKeyBeginLine().IsMatch(privateKeyPem);
    }

    /// <summary>
    /// The BEGIN line of every private key form: <c>RSA PRIVATE KEY</c>, <c>EC PRIVATE KEY</c>, the
    /// generic PKCS#8 <c>PRIVATE KEY</c> and its <c>ENCRYPTED</c> form.
    /// </summary>
    /// <returns>The generated matcher.</returns>
    [GeneratedRegex("-----BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY-----")]
    private static partial Regex PrivateKeyBeginLine();
}
