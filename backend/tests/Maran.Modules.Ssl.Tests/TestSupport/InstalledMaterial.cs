using Maran.Agent.Client.Services.SitesService;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>One install the agent double was asked to perform, recorded whole.</summary>
/// <remarks>
/// A named record rather than a five-part tuple, because a tuple of five values — two of which are
/// both strings holding PEM text — is read positionally, and a test that asserted on the wrong one
/// would look right.
/// </remarks>
/// <param name="Account">The system user name the install was addressed to.</param>
/// <param name="Domain">The domain installed for.</param>
/// <param name="CertificatePem">The certificate handed to the agent.</param>
/// <param name="PrivateKeyPem">The key handed to the agent.</param>
/// <param name="Site">The descriptor the rewritten vhost is rendered from.</param>
public sealed record InstalledMaterial(
    string Account,
    string Domain,
    string CertificatePem,
    string PrivateKeyPem,
    SiteDescriptor Site);
