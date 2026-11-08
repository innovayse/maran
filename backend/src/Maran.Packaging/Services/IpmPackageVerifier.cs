using Maran.Packaging.Interfaces;
using Maran.Packaging.Models;
using Maran.Packaging.Resources;

namespace Maran.Packaging.Services;

/// <summary>
/// Runs every check a package must pass, in the order that never believes unauthenticated bytes:
/// the archive is read within its caps; the signature over the stored manifest bytes is checked
/// BEFORE the manifest is parsed; then the manifest is read, judged by the module rules, fitted to the
/// panel and (when an installation is given) to the installation; and last, each payload byte is held
/// to the manifest's pin. Nothing is written to disk and nothing is loaded: the answer is data.
/// </summary>
/// <remarks>
/// A passing result says the package is the one the key holder signed and that it declares nothing the
/// module rules forbid. It does NOT say the code is safe: a signed module is trusted code, in-process,
/// as the panel's service account. The key is the verifier's; a different key (a rotated one, a
/// throwaway in a test) is a different <see cref="IIpmSignatureVerifier"/>.
/// </remarks>
public sealed class IpmPackageVerifier
{
    /// <summary>Reads the archive within its caps.</summary>
    private readonly IpmReader _reader;

    /// <summary>Judges the signature with the supplied key.</summary>
    private readonly IIpmSignatureVerifier _signatures;

    /// <summary>Creates a verifier.</summary>
    /// <param name="reader">The archive reader, with the caps it enforces.</param>
    /// <param name="signatures">The signature check, built from the key the caller pins.</param>
    public IpmPackageVerifier(IpmReader reader, IIpmSignatureVerifier signatures)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(signatures);
        _reader = reader;
        _signatures = signatures;
    }

    /// <summary>Verifies a package.</summary>
    /// <param name="package">A readable stream holding the <c>.ipm</c>; not disposed.</param>
    /// <param name="context">What the package is judged against.</param>
    /// <returns>The verified package, or the first refusal.</returns>
    public Result<VerifiedIpmPackage> Verify(Stream package, IpmVerificationContext context)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(context);

        var read = _reader.Read(package);

        if (!read.IsSuccess)
        {
            return Result<VerifiedIpmPackage>.Fail(read.Error!);
        }

        var archive = read.Value;

        if (!_signatures.Verify(archive.ManifestBytes, archive.SignatureBytes))
        {
            return Fail(Error.Of(nameof(ErrorMessages.IpmSignatureInvalid), ErrorType.Validation));
        }

        var parsed = IpmManifestCodec.Parse(archive.ManifestBytes);

        if (!parsed.IsSuccess)
        {
            return Fail(parsed.Error!);
        }

        var judged = IpmManifestValidator.Validate(parsed.Value, context.PanelVersion);

        if (!judged.IsSuccess)
        {
            return Fail(judged.Error!);
        }

        var manifest = judged.Value;
        var refusal = CheckFit(manifest, context) ?? IpmPayloadPins.Check(manifest, archive).Error;

        return refusal is null
            ? Result<VerifiedIpmPackage>.Ok(new VerifiedIpmPackage(manifest, archive))
            : Fail(refusal);
    }

    /// <summary>Whether the manifest fits the panel and, when one is given, the installation.</summary>
    /// <param name="manifest">The authenticated, valid manifest.</param>
    /// <param name="context">What it is judged against.</param>
    /// <returns>Null when it fits; otherwise the refusal.</returns>
    private static Error? CheckFit(IpmManifest manifest, IpmVerificationContext context)
    {
        var panel = IpmCompatibility.CheckPanel(manifest, context.PanelVersion, context.Now);

        if (!panel.IsSuccess || context.InstalledModules is null)
        {
            return panel.Error;
        }

        return IpmCompatibility.CheckInstall(manifest, context.InstalledModules, context.Downgrade).Error;
    }

    /// <summary>A failed result from an error.</summary>
    /// <param name="error">The refusal.</param>
    /// <returns>The failed result.</returns>
    private static Result<VerifiedIpmPackage> Fail(Error error)
    {
        return Result<VerifiedIpmPackage>.Fail(error);
    }
}
