using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Maran.Agent.Client.Interfaces;
using Maran.Modules.Ssl.Models;
using Maran.Modules.Ssl.Options;
using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Time;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Ssl.Tests.Integration;

/// <summary>
/// Runs the production <see cref="AcmeClient"/> — unmodified, over the production
/// <see cref="AcmeAccountStore"/>, <see cref="AcmeChallengeWriter"/>, <see cref="AcmeSession"/>,
/// <see cref="AcmeSigner"/> and <see cref="AcmeTransport"/> — against a real, running pebble
/// (Let's Encrypt's own ACME test server) instead of a fake authority.
/// </summary>
/// <remarks>
/// This is deliberately NOT part of the ordinary `dotnet test` gate: it needs
/// <c>docker compose -f docker/docker-compose.pebble.yml up -d</c> already running on the same host,
/// and it binds a real TCP listener on port 5002. Skipped unless
/// <c>MARAN_PEBBLE_TESTS=1</c> is set, so CI and every ordinary local run never touch the network.
///
/// The only test double left in is <see cref="IAgentFilesClient"/> — the real one talks to the root
/// agent over a unix socket to write into a customer's home directory, which is the other lane's
/// surface (agent accounts/ops) and out of this slice's scope. The double here performs a REAL
/// filesystem write to a temp directory that a real <see cref="HttpListener"/> serves on
/// 127.0.0.1:5002, so the HTTP-01 request pebble makes is answered by an actual file on an actual
/// disk, the same shape as production, just not written by the agent.
///
/// <para>
/// <b>Which pebble knobs are set, and what each one hides.</b> `PEBBLE_WFE_NONCEREJECT=0` stops
/// pebble rejecting a fraction of requests with a bad nonce, so the client's retry path is NOT
/// exercised here. `PEBBLE_ALTERNATE_ROOTS=0` hides chain-root variability.
/// `PEBBLE_AUTHZREUSE=0` forces a fresh HTTP-01 every run, which means the opposite paths — an
/// order or authorization already valid, the renewal fast path — are also NOT proven here.
/// `PEBBLE_VA_ALWAYS_VALID` is deliberately left OFF: it would skip validation altogether and turn
/// this test into a check that pebble answers, rather than that the challenge was really served.
/// </para>
/// <para>
/// <b>Trust is by configuration, never by disabling verification.</b> An <c>X509Chain</c> pins
/// <c>CustomRootTrust</c> to exactly <c>docker/pebble/pebble-minica.pem</c> — the fixed root that
/// signs pebble's directory TLS, not the issuance root, which pebble regenerates on every start.
/// Pinning the wrong one of those two produces `PartialChain: unable to get local issuer
/// certificate`, which is how the distinction was learnt. Nothing in production code or any
/// <c>Ssl</c> HttpClient registration is relaxed to make this work.
/// </para>
/// <para>
/// <b>What this does NOT prove.</b> pebble implements the ACME protocol; it is not Let's Encrypt's
/// service, its rate limits, its validation from the public internet, or its chain. A publicly
/// resolvable domain with inbound reachability is still owed.
/// </para>
/// </remarks>
public sealed class PebbleAcmeIssuanceTests
{
    /// <summary>Where pebble's directory document lives, per its unmodified default config.</summary>
    private const string DirectoryUrl = "https://localhost:14000/dir";

    /// <summary>Where pebble's management API — and its root certificate — live.</summary>
    /// <summary>
    /// Path, relative to the repository root, of the root that signs pebble's OWN directory and
    /// management HTTPS endpoints (test/certs/pebble.minica.pem inside the image — a fixed file
    /// baked into every build of it, unlike the issuance root pebble generates fresh on every
    /// start). Copied out with <c>docker cp</c> once; see docker/docker-compose.pebble.yml.
    /// </summary>
    private const string PebbleServerRootRelativePath = "docker/pebble/pebble-minica.pem";

    /// <summary>The domain requested. Real DNS resolves it to the loopback pebble itself runs on.</summary>
    private const string Domain = "localhost";

    /// <summary>A real, plausible operator address — the fact established up front was that the
    /// default "admin@localhost" is refused by a real authority at registration.</summary>
    private const string ContactEmail = "acme-test@maran-panel.invalid";

    /// <summary>Runs one full issuance against pebble and writes a transcript to disk.</summary>
    [Fact]
    public async Task Issues_one_certificate_against_a_real_pebble_server()
    {
        if (Environment.GetEnvironmentVariable("MARAN_PEBBLE_TESTS") != "1")
        {
            return; // Skipped: no live pebble expected. Set MARAN_PEBBLE_TESTS=1 to run for real.
        }

        var transcript = new StringBuilder();
        void Log(string line)
        {
            transcript.AppendLine(line);
            Console.WriteLine(line);
        }

        // Trust pebble's root BY CONFIGURATION: read the ONE certificate that signs pebble's own
        // directory/management HTTPS endpoint (a fixed file inside the image, copied out to
        // docker/pebble/pebble-minica.pem — see docker/docker-compose.pebble.yml), and accept a TLS
        // connection only when the server's chain resolves to EXACTLY that certificate — never a
        // blanket ServerCertificateCustomValidationCallback => true. This can only ever affect the
        // "acme" named HttpClient this test builds for itself below; it is never wired into
        // Maran.Host's real HttpClient registration, so it cannot leak into a production install.
        var repositoryRoot = FindRepositoryRoot();
        var pebbleRootPath = Path.Combine(repositoryRoot, PebbleServerRootRelativePath);
        using var pebbleRoot = X509Certificate2.CreateFromPem(File.ReadAllText(pebbleRootPath));
        Log($"[trust] read pebble's server root from {pebbleRootPath}");
        Log($"[trust] pinned root: subject={pebbleRoot.Subject} issuer={pebbleRoot.Issuer} thumbprint={pebbleRoot.Thumbprint}");

        using var pinnedRoot = new X509Certificate2(pebbleRoot);
        var acmeHandler = new HttpClientHandler
        {
            // Re-builds the chain against ONLY the fetched-and-pinned pebble root as a custom trust
            // anchor — not "accept anything", and not the machine's real trust store either: a
            // pebble server presenting a DIFFERENT root (or a leaf with no path to this one) is
            // still refused. This is the "by configuration" trust the task asked to distinguish from
            // disabling verification.
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

        // Serve the HTTP-01 challenge file for real, from a real temp directory, over a real
        // HttpListener on the exact port pebble's default config validates against (5002). This is
        // the one piece production does differently: there, the agent writes the file into a
        // customer's document root over the unix socket, and nginx serves it. Nothing about
        // AcmeClient, AcmeChallengeWriter or the wire protocol below is different from production.
        var documentRoot = Path.Combine(Path.GetTempPath(), "maran-pebble-test-" + Guid.NewGuid());
        Directory.CreateDirectory(documentRoot);
        using var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:5002/");
        listener.Start();
        Log("[challenge-server] listening on http://localhost:5002/ serving " + documentRoot);
        var serverCts = new CancellationTokenSource();
        var serverTask = ServeAsync(listener, documentRoot, Log, serverCts.Token);

        try
        {
            using var dbContext = SslTestContext.Create(FakeCurrentUser.Admin());
            var clock = new SystemClock();
            var files = new LocalFilesystemFilesClient(documentRoot, Log);
            var factory = new SingleHandlerHttpClientFactory(acmeHandler);
            var options = new OptionsWrapper<AcmeOptions>(new AcmeOptions
            {
                DirectoryUrl = DirectoryUrl,
                ContactEmail = ContactEmail,
                ValidationTimeoutSeconds = 60,
                PollIntervalSeconds = 1,
            });

            var client = new AcmeClient(
                factory,
                options,
                new AcmeChallengeWriter(files),
                new AcmeAccountStore(dbContext, clock, NullLogger<AcmeAccountStore>.Instance),
                clock,
                NullLogger<AcmeClient>.Instance);

            Log($"[order] requesting domain={Domain} contact={ContactEmail} directory={DirectoryUrl}");
            var result = await client.OrderAsync(
                new AcmeOrderRequest(Domain, "acme-test-account"),
                CancellationToken.None);

            if (!result.IsSuccess)
            {
                Log($"[FAILED] stage error code = {result.Error!.Code}");
                await File.WriteAllTextAsync(TranscriptPath(), transcript.ToString());
                Assert.Fail("Pebble issuance failed: " + result.Error!.Code);
                return;
            }

            Log("[order] AcmeClient.OrderAsync returned success");

            var certPath = Path.Combine(Path.GetTempPath(), "maran-pebble-cert-" + Guid.NewGuid() + ".pem");
            await File.WriteAllTextAsync(certPath, result.Value.CertificatePem);
            Log($"[certificate] wrote issued chain to {certPath}");

            using var leaf = X509Certificate2.CreateFromPem(result.Value.CertificatePem);
            Log($"[certificate] subject={leaf.Subject}");
            Log($"[certificate] issuer={leaf.Issuer}");
            Log($"[certificate] notBefore={leaf.NotBefore:o} notAfter={leaf.NotAfter:o}");
            Log($"[certificate] serial={leaf.SerialNumber}");

            Assert.Equal(Domain, leaf.GetNameInfo(X509NameType.DnsName, false));
            Assert.True(new DateTimeOffset(leaf.NotAfter.ToUniversalTime(), TimeSpan.Zero) > clock.UtcNow);

            await File.WriteAllTextAsync(TranscriptPath(), transcript.ToString());
        }
        finally
        {
            serverCts.Cancel();
            listener.Stop();
            try
            {
                await serverTask;
            }
            catch (Exception)
            {
                // Best-effort shutdown of the throwaway challenge server; nothing to assert on it.
            }

            Directory.Delete(documentRoot, recursive: true);
        }
    }

    /// <summary>Walks up from the test binary's directory to the repository root (marked by `.git`).</summary>
    /// <returns>The repository root's absolute path.</returns>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root (.git) not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Where this run's transcript is written, for the report to quote verbatim.</summary>
    private static string TranscriptPath()
    {
        return Path.Combine(Path.GetTempPath(), "maran-pebble-transcript.log");
    }

    /// <summary>Answers every GET under the challenge directory from files on disk, until cancelled.</summary>
    private static async Task ServeAsync(HttpListener listener, string documentRoot, Action<string> log, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                var getContextTask = listener.GetContextAsync();
                var completed = await Task.WhenAny(getContextTask, Task.Delay(Timeout.Infinite, cancellationToken));
                if (completed != getContextTask)
                {
                    return;
                }

                context = await getContextTask;
            }
            catch (Exception)
            {
                return;
            }

            var relative = context.Request.Url!.AbsolutePath.TrimStart('/');
            var filePath = Path.Combine(documentRoot, relative);
            if (File.Exists(filePath))
            {
                var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
                context.Response.StatusCode = 200;
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
                log($"[challenge-server] 200 GET {context.Request.Url.AbsolutePath}");
            }
            else
            {
                context.Response.StatusCode = 404;
                log($"[challenge-server] 404 GET {context.Request.Url.AbsolutePath}");
            }

            context.Response.OutputStream.Close();
        }
    }

    /// <summary>An <see cref="IHttpClientFactory"/> handing out one client over one handler.</summary>
    private sealed class SingleHandlerHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public SingleHandlerHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(_handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };

            // Mirrors the real registration this test does not go through (Maran.Modules.Ssl's
            // SslModule.AddHttpClient(AcmeOptions.HttpClientName, ...)): pebble refuses every request
            // with no User-Agent as "malformed", which is the gap this test surfaced.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Maran-Panel-Acme-Client/1.0");
            return client;
        }
    }

    /// <summary>
    /// An <see cref="IAgentFilesClient"/> double that performs a REAL write to a REAL temp directory
    /// instead of crossing the agent's unix socket, so pebble's HTTP-01 fetch is answered by an
    /// actual file. <see cref="AcmeChallengeWriter"/>'s path-building and validation run unmodified.
    /// </summary>
    private sealed class LocalFilesystemFilesClient : IAgentFilesClient
    {
        private readonly string _documentRoot;
        private readonly Action<string> _log;

        public LocalFilesystemFilesClient(string documentRoot, Action<string> log)
        {
            _documentRoot = documentRoot;
            _log = log;
        }

        public Task<Result<ulong>> WriteFileAsync(
            string accountUsername, string path, string content, uint mode, CancellationToken cancellationToken)
        {
            // AcmeChallengeWriter hands us "sites/<domain>/.well-known/acme-challenge/<token>",
            // home-relative. This double's "home" is the document root the test's HttpListener
            // serves from "/" — i.e. it strips the "sites/<domain>/" prefix the same way nginx's
            // vhost root does in production, and serves the rest.
            var withoutSitesPrefix = StripSitesPrefix(path);
            var fullPath = Path.Combine(_documentRoot, withoutSitesPrefix);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
            _log($"[challenge-writer] wrote {fullPath} ({content.Length} bytes) for account={accountUsername}");
            return Task.FromResult(Result<ulong>.Ok((ulong)content.Length));
        }

        public Task<Result<bool>> DeleteEntryAsync(
            string accountUsername, string path, bool recursive, CancellationToken cancellationToken)
        {
            var fullPath = Path.Combine(_documentRoot, StripSitesPrefix(path));
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            _log($"[challenge-writer] removed {fullPath} for account={accountUsername}");
            return Task.FromResult(Result<bool>.Ok(true));
        }

        private static string StripSitesPrefix(string path)
        {
            var segments = path.Split('/');
            // segments[0] = "sites", segments[1] = "<domain>", the rest is ".well-known/acme-challenge/<token>".
            return string.Join('/', segments.Skip(2));
        }
    }
}
