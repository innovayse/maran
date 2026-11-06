using Maran.Modules.Ssl.Resources;

namespace Maran.Modules.Ssl.Services;

/// <summary>
/// Describes a certificate's stored renewal failure for the current request's culture: the sentence
/// a person reads, resolved when the certificate is READ.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same resolution every problem response uses.</b> A renewal fails with an error code some
/// module raised — the Ssl module's own, the agent client's — and the sentence for that code is that
/// module's <c>ErrorMessages</c> entry, read through the one <see cref="IErrorTextProvider"/> the
/// panel composes from every module's tables. The row keeps only the code, so a reader who switches
/// language is answered in the new one; a sentence stored at write time would be frozen in the
/// language of whoever happened to trigger the renewal (rules/csharp.md, a stored failure is resolved
/// when it is read).
/// </para>
/// <para>
/// <b>A code no loaded table describes reads as a generic sentence, never as the code.</b> A row may
/// carry a code a build no longer ships. The reader is then told that renewal failed and that the
/// panel will try again — <c>CertificateRenewalFailedGeneric</c> — instead of a machine constant
/// where a sentence belongs.
/// </para>
/// </remarks>
public sealed class CertificateRenewalFailureMessages
{
    /// <summary>The panel's one resolution from an error code to its sentence.</summary>
    private readonly IErrorTextProvider _errorTexts;

    /// <summary>Creates the describer.</summary>
    /// <param name="errorTexts">The panel's error-message resolution, composed from every module's tables.</param>
    public CertificateRenewalFailureMessages(IErrorTextProvider errorTexts)
    {
        _errorTexts = errorTexts;
    }

    /// <summary>Describes one stored renewal failure, or nothing when the certificate has none.</summary>
    /// <param name="errorCode">The code the row stores, or <c>null</c> while no renewal has failed.</param>
    /// <returns>The sentence in the request's culture, or <c>null</c> when there is no failure.</returns>
    public string? Describe(string? errorCode)
    {
        if (errorCode is null)
        {
            return null;
        }

        return _errorTexts.TryResolve(errorCode, out var text)
            ? text
            : _errorTexts.Resolve(nameof(ErrorMessages.CertificateRenewalFailedGeneric));
    }
}
