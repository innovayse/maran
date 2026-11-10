using System.Security.Cryptography.X509Certificates;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// How the opt-in Pebble tests trust the test server: by pinning the one certificate that signs Pebble's own
/// directory endpoint, never by switching verification off.
/// </summary>
/// <remarks>
/// Shared by the HTTP-01 and the DNS-01 Pebble tests, which would otherwise each carry the same page of trust
/// code. It can only ever affect the <c>acme</c> client those tests build for themselves; nothing here is wired
/// into the Host's real client registration, so it cannot leak into a production install.
/// </remarks>
public static class PebbleServerTrust
{
    /// <summary>
    /// Path, relative to the repository root, of the root that signs Pebble's OWN directory and management
    /// HTTPS endpoints (a fixed file baked into every build of the image, unlike the issuance root Pebble
    /// regenerates on every start). Copied out with <c>docker cp</c> once; see docker/docker-compose.pebble.yml.
    /// </summary>
    public const string ServerRootRelativePath = "docker/pebble/pebble-minica.pem";

    /// <summary>Builds a handler that accepts a TLS connection only when the server's chain resolves to exactly the pinned root.</summary>
    /// <param name="pinnedRoot">The one certificate allowed as the root of Pebble's endpoint.</param>
    /// <returns>The handler to build the test's <c>acme</c> client over.</returns>
    /// <remarks>
    /// The chain is rebuilt against ONLY the pinned root as a custom trust anchor: not "accept anything", and
    /// not the machine's real trust store either, so a server presenting a different root is still refused.
    /// </remarks>
    public static HttpClientHandler PinnedHandler(X509Certificate2 pinnedRoot)
    {
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null)
                {
                    return false;
                }

                using var leafCertificate = new X509Certificate2(certificate);
                using var validationChain = new X509Chain();
                validationChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                validationChain.ChainPolicy.CustomTrustStore.Add(pinnedRoot);
                validationChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                return validationChain.Build(leafCertificate);
            },
        };
    }

    /// <summary>Walks up from the test binary's directory to the repository root (marked by <c>.git</c>).</summary>
    /// <returns>The repository root's absolute path.</returns>
    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root (.git) not found above " + AppContext.BaseDirectory);
    }
}
