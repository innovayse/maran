using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Services.SslService;

/// <summary>
/// Turns the agent's verdict on certificate material a customer supplied into the error code the
/// panel answers with: one code per verdict, so the screen can say which half of the pair to fix.
/// </summary>
/// <remarks>
/// Every verdict is <see cref="ErrorType.Validation"/>: the agent read the material, refused it, and
/// wrote nothing, so the request was wrong and resending it unchanged will be refused again. A
/// reason this panel does not know — <c>Unspecified</c>, or a value a newer agent added — is not a
/// verdict it can name, and is answered as the agent sending something this panel cannot read
/// rather than being guessed into one of the three.
/// </remarks>
internal static class CertificateMaterialRefusalTranslator
{
    /// <summary>Maps one refusal reason onto its error.</summary>
    /// <param name="reason">The reason the agent gave.</param>
    /// <returns>The typed error the panel answers with.</returns>
    public static Error ToError(CertificateMaterialRefusal reason)
    {
        return reason switch
        {
            CertificateMaterialRefusal.MalformedCertificate => Error.Of(
                nameof(ErrorMessages.AgentCertificateUnreadable), ErrorType.Validation),
            CertificateMaterialRefusal.MalformedPrivateKey => Error.Of(
                nameof(ErrorMessages.AgentPrivateKeyUnreadable), ErrorType.Validation),
            CertificateMaterialRefusal.KeyDoesNotMatchCertificate => Error.Of(
                nameof(ErrorMessages.AgentPrivateKeyMismatch), ErrorType.Validation),
            _ => Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure),
        };
    }
}
