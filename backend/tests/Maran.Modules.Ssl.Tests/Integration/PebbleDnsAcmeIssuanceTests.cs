using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using Maran.Modules.Ssl.Models;
using Maran.Modules.Ssl.Options;
using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Time;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Ssl.Tests.Integration;

/// <summary>
/// Runs the production <see cref="AcmeClient"/> for a WILDCARD order against a real, running Pebble that
/// validates DNS-01 through <c>pebble-challtestsrv</c>, the mock DNS server Pebble's own tests use.
/// </summary>
/// <remarks>
/// <para>
/// Opt-in exactly like <see cref="PebbleAcmeIssuanceTests"/>: it needs
/// <c>docker compose -f docker/docker-compose.pebble.yml up -d</c> (which now starts the mock DNS server and
/// points Pebble at it) and is skipped unless <c>MARAN_PEBBLE_TESTS=1</c>.
/// </para>
/// <para>
/// <b>What is real and what is a double.</b> Everything in the ACME conversation is production code: the
/// client, the account store, the signer, the DNS-01 responder and the writer that words the door's refusals.
/// The one double is the DNS module's door: in production a TXT record goes into a zone the name server
/// serves, here it goes into the mock DNS server's management API, which Pebble then queries. So this proves
/// the wildcard order against a real ACME implementation; it does NOT prove that the panel's own name server
/// answers for the record, which the agent's real-host suite owns.
/// </para>
/// <para>
/// <b>Not observed when this was written.</b> The author could not start the compose stack in the session that
/// wrote it, so the test compiles and skips, and has not been run against Pebble.
/// </para>
/// </remarks>
public sealed class PebbleDnsAcmeIssuanceTests
{
    /// <summary>Where pebble's directory document lives, per its default config.</summary>
    private const string DirectoryUrl = "https://localhost:14000/dir";

    /// <summary>Where the mock DNS server's management API lives, per its default flags.</summary>
    private const string ManagementUrl = "http://localhost:8055";

    /// <summary>The domain requested; the mock DNS server answers any TXT query for it from what was set.</summary>
    private const string Domain = "dns01.example.test";

    /// <summary>A plausible operator address; a real authority refuses the default <c>admin@localhost</c>.</summary>
    private const string ContactEmail = "acme-test@maran-panel.invalid";

    /// <summary>Issues one wildcard certificate against Pebble through the mock DNS server.</summary>
    [Fact]
    public async Task Issues_one_wildcard_certificate_against_a_real_pebble_server()
    {
        if (Environment.GetEnvironmentVariable("MARAN_PEBBLE_TESTS") != "1")
        {
            return; // Skipped: no live pebble expected. Set MARAN_PEBBLE_TESTS=1 to run for real.
        }

        var pebbleRootPath = Path.Combine(PebbleServerTrust.FindRepositoryRoot(), PebbleServerTrust.ServerRootRelativePath);
        using var pebbleRoot = X509Certificate2.CreateFromPem(File.ReadAllText(pebbleRootPath));
        using var pinnedRoot = new X509Certificate2(pebbleRoot);
        using var acmeHandler = PebbleServerTrust.PinnedHandler(pinnedRoot);
        using var management = new HttpClient { BaseAddress = new Uri(ManagementUrl) };
        using var dbContext = SslTestContext.Create(FakeCurrentUser.Admin());
        var clock = new SystemClock();
        var records = new ChallengeTestServerRecords(management);

        var client = new AcmeClient(
            new SingleHandlerHttpClientFactory(acmeHandler),
            new OptionsWrapper<AcmeOptions>(new AcmeOptions
            {
                DirectoryUrl = DirectoryUrl,
                ContactEmail = ContactEmail,
                ValidationTimeoutSeconds = 60,
                PollIntervalSeconds = 1,
            }),
            new AcmeChallengeWriter(new RecordingAgentFilesClient(), new RecordingAgentSslClient()),
            new AcmeDnsChallengeWriter([records]),
            new AcmeAccountStore(dbContext, clock, NullLogger<AcmeAccountStore>.Instance),
            clock,
            NullLogger<AcmeClient>.Instance);

        var result = await client.OrderAsync(
            new AcmeOrderRequest(Domain, new DnsZoneChallenge(Guid.NewGuid())), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Error!.Code);
        using var leaf = X509Certificate2.CreateFromPem(result.Value.CertificatePem);
        var names = leaf.Extensions.OfType<X509SubjectAlternativeNameExtension>()
            .SelectMany(extension =>
            {
                return extension.EnumerateDnsNames();
            })
            .ToList();
        Assert.Contains(Domain, names);
        Assert.Contains("*." + Domain, names);
        Assert.Equal(2, records.PublishedCount);
        Assert.Equal(2, records.RemovedCount);
    }

    /// <summary>
    /// The DNS module's door as the mock DNS server sees it: a TXT record goes into its management API instead of
    /// into a zone, so Pebble's validation authority finds it when it asks.
    /// </summary>
    private sealed class ChallengeTestServerRecords : IDnsChallengeRecords
    {
        /// <summary>The mock DNS server's management client.</summary>
        private readonly HttpClient _management;

        /// <summary>Creates the door over the management client.</summary>
        /// <param name="management">The mock DNS server's management client.</param>
        public ChallengeTestServerRecords(HttpClient management)
        {
            _management = management;
        }

        /// <summary>How many records were set.</summary>
        public int PublishedCount { get; private set; }

        /// <summary>How many records were cleared.</summary>
        public int RemovedCount { get; private set; }

        /// <inheritdoc />
        public Task<DnsChallengeAvailability> CheckAsync(Guid accountId, string hostname, CancellationToken cancellationToken)
        {
            return Task.FromResult(DnsChallengeAvailability.Available);
        }

        /// <inheritdoc />
        public async Task<Result<DnsChallengeHandle>> PublishAsync(
            Guid accountId,
            string hostname,
            string txtValue,
            CancellationToken cancellationToken)
        {
            using var response = await _management.PostAsJsonAsync(
                "/set-txt", new { host = $"_acme-challenge.{hostname}.", value = txtValue }, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result<DnsChallengeHandle>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));
            }

            PublishedCount++;
            return Result<DnsChallengeHandle>.Ok(new DnsChallengeHandle(accountId, Guid.NewGuid(), Guid.NewGuid()));
        }

        /// <inheritdoc />
        public async Task<Result> RemoveAsync(DnsChallengeHandle handle, CancellationToken cancellationToken)
        {
            // The mock clears every value of a host at once, so the second removal clears nothing; both are counted
            // because the client asked for both.
            using var response = await _management.PostAsJsonAsync(
                "/clear-txt", new { host = $"_acme-challenge.{Domain}." }, cancellationToken);
            RemovedCount++;

            return response.IsSuccessStatusCode ? Result.Ok() : Result.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));
        }
    }
}
