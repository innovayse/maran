using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Maran.Modules.Licensing.Options;
using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;
using Maran.Sdk.Modules;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers <see cref="LeaseRequestAuthorizer"/> over the real licence verifier, lease source and coverage
/// cache: a good lease is attached as a bearer the licence service can verify with its own key, and every
/// state in which the panel has no usable lease attaches nothing and says so. The stand-in the Sdk
/// registers when nothing else does is the control that attaches nothing at all.
/// </summary>
public sealed class LeaseRequestAuthorizerTests : IDisposable
{
    /// <summary>The instant the clock starts at; the licence and lease are placed around it.</summary>
    private static readonly DateTimeOffset Now = CoverageHarness.Start;

    /// <summary>A directory of its own, removed afterwards.</summary>
    private readonly string _directory =
        Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "lease-authorizer-tests-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>The clock the cover is judged by.</summary>
    private readonly MutableClock _clock = new(Now);

    /// <summary>The licence file path.</summary>
    private string LicencePath
    {
        get { return Path.Combine(_directory, "licence.json"); }
    }

    /// <summary>The lease file path.</summary>
    private string LeasePath
    {
        get { return Path.Combine(_directory, "lease.json"); }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Writes a valid, online licence naming the module <c>mail</c>.</summary>
    /// <param name="offline">True to write an offline licence, which needs and holds no lease.</param>
    private void InstallLicence(bool offline = false)
    {
        File.WriteAllText(
            LicencePath,
            LicenceEnvelopeBuilder.Build(
                TestKeys.RealPrivateKeyHex,
                modules: ["mail"],
                expiry: Now.AddDays(90),
                offline: offline ? true : null));
    }

    /// <summary>Writes a lease signed by the test lease seed.</summary>
    /// <param name="issued">When it was issued.</param>
    /// <param name="validUntil">When it ends.</param>
    /// <param name="licence">The licence id it renews.</param>
    /// <param name="privateKeyHex">The seed it is signed with.</param>
    private void InstallLease(
        DateTimeOffset issued,
        DateTimeOffset validUntil,
        string licence = "lic-001",
        string? privateKeyHex = null)
    {
        File.WriteAllText(
            LeasePath,
            LeaseEnvelopeBuilder.Build(privateKeyHex ?? TestKeys.LeasePrivateKeyHex, licence, issued, validUntil));
    }

    /// <summary>Builds the authorizer over the files and the clock of this test.</summary>
    /// <returns>The authorizer.</returns>
    private LeaseRequestAuthorizer Authorizer()
    {
        var identity = new FixedServerIdentitySource();
        var leaseSource = new FileLeaseSource(
            new OptionsWrapper<LeaseStorageOptions>(new LeaseStorageOptions { LeaseFilePath = LeasePath }),
            new Ed25519LeaseSignatureVerifier(TestKeys.TrustedLeaseKey));
        var verifier = new LicenceVerifier(new Ed25519LicenceSignatureVerifier(TestKeys.TrustedKey), _clock, identity);
        var source = new LicenceCoverageSource(
            new FileLicenceRawTextSource(
                new OptionsWrapper<LicenceStorageOptions>(new LicenceStorageOptions { LicenceFilePath = LicencePath })),
            verifier,
            leaseSource,
            identity,
            _clock);

        return new LeaseRequestAuthorizer(new LicenceCoverageCache(source, _clock), leaseSource);
    }

    /// <summary>A request to the licence-bound service.</summary>
    /// <returns>The request, with no authorization header.</returns>
    private static HttpRequestMessage Request()
    {
        return new HttpRequestMessage(HttpMethod.Get, "https://marketplace.test/api/v1/catalogue");
    }

    /// <summary>A fresh lease is attached as a bearer whose signature the licence service verifies with its own key over the payload it issued.</summary>
    [Fact]
    public async Task A_good_lease_is_attached_as_a_bearer_the_service_can_verify()
    {
        InstallLicence();
        InstallLease(Now, Now.AddDays(14));
        using var request = Request();

        var attached = await Authorizer().TryAuthorizeAsync(request, CancellationToken.None);

        Assert.True(attached);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        var parts = request.Headers.Authorization.Parameter!.Split('.');
        Assert.Equal(2, parts.Length);
        var payload = Base64Url.DecodeFromChars(parts[0]);
        var signature = Base64Url.DecodeFromChars(parts[1]);

        var publicKey = new Ed25519PrivateKeyParameters(Convert.FromHexString(TestKeys.LeasePrivateKeyHex), 0)
            .GeneratePublicKey();
        var verifier = new Ed25519Signer();
        verifier.Init(forSigning: false, publicKey);
        verifier.BlockUpdate(payload, 0, payload.Length);
        Assert.True(verifier.VerifySignature(signature));

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(payload));
        Assert.Equal("lease", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("lic-001", document.RootElement.GetProperty("licence").GetString());
    }

    /// <summary>The credential carries nothing but the lease: the headers of the request are the authorization and nothing else.</summary>
    [Fact]
    public async Task Nothing_but_the_authorization_header_is_added()
    {
        InstallLicence();
        InstallLease(Now, Now.AddDays(14));
        using var request = Request();

        await Authorizer().TryAuthorizeAsync(request, CancellationToken.None);

        Assert.Equal("Authorization", Assert.Single(request.Headers).Key);
        Assert.Null(request.Content);
        Assert.Equal("https://marketplace.test/api/v1/catalogue", request.RequestUri!.ToString());
    }

    /// <summary>A lease whose daily renewal is late but which still holds keeps its credential: the cover continues for its life.</summary>
    [Fact]
    public async Task A_lease_whose_renewal_is_late_but_still_holds_is_attached()
    {
        InstallLicence();
        InstallLease(Now.AddDays(-3), Now.AddDays(11));
        using var request = Request();

        Assert.True(await Authorizer().TryAuthorizeAsync(request, CancellationToken.None));
        Assert.NotNull(request.Headers.Authorization);
    }

    /// <summary>With no lease stored, nothing is attached and the answer says so, so the caller reports LeaseRequired.</summary>
    [Fact]
    public async Task No_stored_lease_attaches_nothing()
    {
        InstallLicence();
        using var request = Request();

        Assert.False(await Authorizer().TryAuthorizeAsync(request, CancellationToken.None));
        Assert.Null(request.Headers.Authorization);
    }

    /// <summary>A lease that has ended attaches nothing, though its file is still there and its signature still holds.</summary>
    [Fact]
    public async Task An_ended_lease_attaches_nothing()
    {
        InstallLicence();
        InstallLease(Now.AddDays(-20), Now.AddDays(-6));
        using var request = Request();

        Assert.False(await Authorizer().TryAuthorizeAsync(request, CancellationToken.None));
        Assert.Null(request.Headers.Authorization);
    }

    /// <summary>A lease the clock outlives stops being attached once the cached answer ages out.</summary>
    [Fact]
    public async Task A_lease_stops_being_attached_when_it_ends()
    {
        InstallLicence();
        InstallLease(Now, Now.AddDays(14));
        var authorizer = Authorizer();
        using var before = Request();
        Assert.True(await authorizer.TryAuthorizeAsync(before, CancellationToken.None));

        _clock.Advance(TimeSpan.FromDays(15));
        using var after = Request();

        Assert.False(await authorizer.TryAuthorizeAsync(after, CancellationToken.None));
        Assert.Null(after.Headers.Authorization);
    }

    /// <summary>A lease for another licence, or signed by another key, is never presented.</summary>
    [Fact]
    public async Task A_lease_for_another_licence_or_signed_by_another_key_attaches_nothing()
    {
        InstallLicence();
        InstallLease(Now, Now.AddDays(14), licence: "lic-999");
        using var foreign = Request();
        Assert.False(await Authorizer().TryAuthorizeAsync(foreign, CancellationToken.None));

        InstallLease(Now, Now.AddDays(14), privateKeyHex: TestKeys.OtherPrivateKeyHex);
        using var forged = Request();
        Assert.False(await Authorizer().TryAuthorizeAsync(forged, CancellationToken.None));

        Assert.Null(foreign.Headers.Authorization);
        Assert.Null(forged.Headers.Authorization);
    }

    /// <summary>An offline licence holds no lease, so there is nothing to present; and with no licence installed nothing is presented either.</summary>
    [Fact]
    public async Task An_offline_licence_and_a_missing_licence_attach_nothing()
    {
        InstallLicence(offline: true);
        InstallLease(Now, Now.AddDays(14));
        using var offline = Request();
        Assert.False(await Authorizer().TryAuthorizeAsync(offline, CancellationToken.None));

        File.Delete(LicencePath);
        using var absent = Request();
        Assert.False(await Authorizer().TryAuthorizeAsync(absent, CancellationToken.None));
    }

    /// <summary>The stand-in the Sdk registers when no lease exists attaches nothing, which is the behaviour this authorizer replaces.</summary>
    [Fact]
    public async Task The_stand_in_attaches_nothing_even_when_a_good_lease_is_stored()
    {
        InstallLicence();
        InstallLease(Now, Now.AddDays(14));
        var standIn = new NoLeaseRequestAuthorizer();
        using var request = Request();

        Assert.False(await standIn.TryAuthorizeAsync(request, CancellationToken.None));
        Assert.Null(request.Headers.Authorization);
    }
}
