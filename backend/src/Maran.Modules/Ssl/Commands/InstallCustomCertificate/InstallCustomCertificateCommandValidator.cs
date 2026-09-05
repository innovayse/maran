using FluentValidation;
using Maran.Modules.Ssl.Resources;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Ssl.Commands.InstallCustomCertificate;

/// <summary>
/// Validates <see cref="InstallCustomCertificateCommand"/> before it reaches the handler
/// (rules/security.md "Input").
/// </summary>
/// <remarks>
/// Shape only. Whether the key actually matches the certificate is decided by the agent, which has
/// the material and the crypto to check it — the panel asserting a match it did not verify would be
/// worse than not checking, because the failure would then surface as nginx refusing to start.
/// </remarks>
public sealed class InstallCustomCertificateCommandValidator
    : AbstractValidator<InstallCustomCertificateCommand>
{
    /// <summary>The armour a PEM certificate block must begin with.</summary>
    private const string CertificatePemPrefix = "-----BEGIN CERTIFICATE-----";

    /// <summary>The largest chain the panel accepts, in characters.</summary>
    private const int MaximumMaterialLength = 65536;

    /// <summary>Configures the field rules for <see cref="InstallCustomCertificateCommand"/>.</summary>
    public InstallCustomCertificateCommandValidator()
    {
        RuleFor(command => command.Domain)
            .NotEmpty()
            .MaximumLength(HostNameRule.MaximumLength)
            .Must(HostNameRule.IsHostName)
            .WithMessage(nameof(ErrorMessages.CertificateDomainInvalidFormat));

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
