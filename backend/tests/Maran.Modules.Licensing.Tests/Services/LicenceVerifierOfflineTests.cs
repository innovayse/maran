using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers the two additions the licence window needed of <see cref="LicenceVerifier"/>: the optional signed
/// <c>offline</c> member (decision D2) and the expired licence being carried on its refusal so the window
/// can say which modules lapsed.
/// </summary>
public sealed class LicenceVerifierOfflineTests
{
    /// <summary>The instant the verifier's clock reads.</summary>
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Builds a verifier over the test key and the fixed instant.</summary>
    /// <returns>The verifier.</returns>
    private static LicenceVerifier Verifier()
    {
        return new LicenceVerifier(
            new Ed25519LicenceSignatureVerifier(TestKeys.TrustedKey),
            new FixedClock(Now),
            new FixedServerIdentitySource());
    }

    /// <summary>A licence with no <c>offline</c> member is online, so every licence issued before the member existed keeps its meaning.</summary>
    [Fact]
    public async Task A_licence_without_the_offline_member_is_online()
    {
        var status = await Verifier().VerifyAsync(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex));

        var valid = Assert.IsType<LicenceStatus.Valid>(status);
        Assert.False(valid.Licence.IsOffline);
    }

    /// <summary>A signed <c>"offline": true</c> makes the licence offline; <c>false</c> is the explicit control.</summary>
    [Fact]
    public async Task A_signed_offline_member_makes_the_licence_offline_and_false_does_not()
    {
        var offline = await Verifier().VerifyAsync(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, offline: true));
        var online = await Verifier().VerifyAsync(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, offline: false));

        Assert.True(Assert.IsType<LicenceStatus.Valid>(offline).Licence.IsOffline);
        Assert.False(Assert.IsType<LicenceStatus.Valid>(online).Licence.IsOffline);
    }

    /// <summary>An <c>offline</c> member that is not a boolean is malformed, never read as true or false.</summary>
    [Fact]
    public async Task A_mistyped_offline_member_is_malformed()
    {
        var payload = $$"""
        {"id":"lic-001","product":"maran","tier":"included","modules":["mail"],"expiry":"{{Now.AddDays(30):O}}","offline":"yes"}
        """;

        var status = await Verifier().VerifyAsync(LicenceEnvelopeBuilder.BuildRaw(TestKeys.RealPrivateKeyHex, payload));

        var refused = Assert.IsType<LicenceStatus.Refused>(status);
        Assert.Equal(LicenceRefusalReason.Malformed, refused.Reason);
    }

    /// <summary>Editing the licence to say it is offline breaks the signature: an administrator cannot switch the lease off.</summary>
    [Fact]
    public async Task Editing_the_offline_member_into_a_signed_licence_breaks_the_signature()
    {
        var online = LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, offline: false);
        var edited = online.Replace("\"offline\":false", "\"offline\":true", StringComparison.Ordinal);

        Assert.NotEqual(online, edited);

        var status = await Verifier().VerifyAsync(edited);

        var refused = Assert.IsType<LicenceStatus.Refused>(status);
        Assert.Equal(LicenceRefusalReason.SignatureInvalid, refused.Reason);
    }

    /// <summary>An expired licence is refused as expired and carries the authentic licence; other refusals carry none.</summary>
    [Fact]
    public async Task An_expired_licence_carries_the_licence_and_other_refusals_do_not()
    {
        var expired = await Verifier().VerifyAsync(
            LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, modules: ["mail"], expiry: Now.AddDays(-1)));
        var forged = await Verifier().VerifyAsync(LicenceEnvelopeBuilder.Build(TestKeys.OtherPrivateKeyHex));

        var lapsed = Assert.IsType<LicenceStatus.Refused>(expired);
        Assert.Equal(LicenceRefusalReason.Expired, lapsed.Reason);
        Assert.Equal(["mail"], lapsed.LapsedLicence!.Modules);
        Assert.Null(Assert.IsType<LicenceStatus.Refused>(forged).LapsedLicence);
    }
}
