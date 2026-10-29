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
/// The certificate and key fields are shape only. Whether the key actually matches the certificate
/// is decided by the agent, which has the material and the crypto to check it — the panel asserting
/// a match it did not verify would be worse than not checking, because the failure would then
/// surface as nginx refusing to start.
/// </para>
/// </remarks>
public sealed class InstallCustomCertificateCommandValidator
    : AbstractValidator<InstallCustomCertificateCommand>
{
    /// <summary>The armour a PEM certificate block must begin with.</summary>
    private const string CertificatePemPrefix = "-----BEGIN CERTIFICATE-----";

    /// <summary>The largest chain the panel accepts, in characters.</summary>
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
            .NotEmpty()
            .MaximumLength(MaximumMaterialLength)
            .Must(BeArmouredCertificate)
            .WithMessage(nameof(ErrorMessages.CertificateMaterialInvalid));

        // Deliberately checks only that something is there and that it is not absurdly large. The
        // key's armour line names its algorithm — RSA, EC, or the generic PKCS#8 form — and pinning
        // one of those here would reject a perfectly good certificate for having been exported by a
        // different tool. The agent verifies that the key matches the certificate, which is the
        // question that actually matters.
        RuleFor(command => command.PrivateKeyPem)
            .NotEmpty()
            .MaximumLength(MaximumMaterialLength)
            .WithMessage(nameof(ErrorMessages.CertificateMaterialInvalid));
    }

    /// <summary>Whether the supplied text at least begins as a PEM certificate.</summary>
    /// <param name="certificatePem">The submitted certificate text.</param>
    /// <returns><c>true</c> when the armour line is present.</returns>
    private static bool BeArmouredCertificate(string certificatePem)
    {
        return certificatePem.Contains(CertificatePemPrefix, StringComparison.Ordinal);
    }
}
