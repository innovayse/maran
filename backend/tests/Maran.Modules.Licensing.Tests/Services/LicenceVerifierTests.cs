using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Proves the mutant table in is not
/// exercised here because it is not implemented in this slice (no data source exists yet —
/// <see cref="LicenceRefusalReason.FingerprintMismatch"/>'s own remarks explain why).
/// </summary>
public sealed class LicenceVerifierTests
{
    /// <summary>A fixed instant used by every test that does not care about the exact clock reading.</summary>
    private static readonly DateTimeOffset FixedReferenceInstant = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A machine-id standing for the host these tests are notionally running on.</summary>
    private const string ThisHostMachineId = "0123456789abcdef0123456789abcdef";

    /// <summary>A machine-id standing for some other server entirely.</summary>
    private const string AnotherHostMachineId = "fedcba9876543210fedcba9876543210";

    /// <summary>Builds a verifier over the real (production-embedded) key and a clock reading the given instant.</summary>
    /// <param name="now">The instant the verifier's clock reports.</param>
    /// <param name="hostMachineId">This host's machine-id, or null for "could not be read".</param>
    private static LicenceVerifier MakeVerifier(DateTimeOffset now, string? hostMachineId = null)
    {
        return new LicenceVerifier(
            new Ed25519LicenceSignatureVerifier(),
            new FixedClock(now),
            new FixedServerIdentitySource(hostMachineId));
    }

    // ---- Mutant #1: signature verification always true --------------------------------------

    /// <summary>Kills the "signature check always true" mutant.</summary>
    [Fact]
    public async Task A_licence_signed_with_the_wrong_key_is_refused_as_SignatureInvalid()
    {
        var envelope = LicenceEnvelopeBuilder.Build(TestKeys.OtherPrivateKeyHex);
        var verifier = MakeVerifier(FixedReferenceInstant);

        var status = await verifier.VerifyAsync(envelope);

        var refused = Assert.IsType<LicenceStatus.Refused>(status);
        Assert.Equal(LicenceRefusalReason.SignatureInvalid, refused.Reason);
    }

    /// <summary>Inverse control for mutant #1: the matching key is accepted.</summary>
    [Fact]
    public async Task A_licence_signed_with_the_real_key_is_accepted()
    {
        var envelope = LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex);
        var verifier = MakeVerifier(FixedReferenceInstant);

        var status = await verifier.VerifyAsync(envelope);

        Assert.IsType<LicenceStatus.Valid>(status);
    }

    // ---- Mutant #2: expiry ignored -----------------------------------------------------------

    /// <summary>Kills the "expiry ignored" mutant.</summary>
    [Fact]
    public async Task A_licence_whose_expiry_is_before_the_clocks_now_is_refused_as_Expired()
    {
        var expiry = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var envelope = LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, expiry: expiry);
        var verifier = MakeVerifier(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var status = await verifier.VerifyAsync(envelope);

        var refused = Assert.IsType<LicenceStatus.Refused>(status);
        Assert.Equal(LicenceRefusalReason.Expired, refused.Reason);
    }

    /// <summary>Inverse control for mutant #2: a licence not yet expired is accepted.</summary>
    [Fact]
    public async Task A_licence_whose_expiry_is_after_the_clocks_now_is_accepted()
    {
        var expiry = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var envelope = LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, expiry: expiry);
        var verifier = MakeVerifier(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var status = await verifier.VerifyAsync(envelope);

        Assert.IsType<LicenceStatus.Valid>(status);
    }

    // ---- Mutant #6: product check ignored ----------------------------------------------------

    /// <summary>Kills the "product check ignored" mutant.</summary>
    [Fact]
    public async Task A_licence_issued_for_a_different_product_is_refused_as_ProductMismatch()
    {
        var envelope = LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, product: "some-other-product");
        var verifier = MakeVerifier(FixedReferenceInstant);

        var status = await verifier.VerifyAsync(envelope);

        var refused = Assert.IsType<LicenceStatus.Refused>(status);
        Assert.Equal(LicenceRefusalReason.ProductMismatch, refused.Reason);
    }

    /// <summary>Inverse control for mutant #6: a licence issued for this product is accepted.</summary>
    [Fact]
    public async Task A_licence_issued_for_this_product_is_accepted()
    {
        var envelope = LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, product: "maran");
        var verifier = MakeVerifier(FixedReferenceInstant);

        var status = await verifier.VerifyAsync(envelope);

        Assert.IsType<LicenceStatus.Valid>(status);
    }

    // ---- Mutant #7: malformed input treated as absent --------------------------------------

    /// <summary>
    /// Kills the "malformed collapsed into Absent" mutant — the brief's own named trap, restated:
    /// "an absent licence must not be the same answer as an invalid one."
    /// </summary>
    [Fact]
    public async Task A_licence_file_that_is_not_valid_JSON_is_refused_as_Malformed_not_reported_as_Absent()
    {
        var verifier = MakeVerifier(FixedReferenceInstant);

        var status = await verifier.VerifyAsync("{ this is not json at all");

        var refused = Assert.IsType<LicenceStatus.Refused>(status);
        Assert.Equal(LicenceRefusalReason.Malformed, refused.Reason);
    }

    /// <summary>Inverse control for mutant #7: genuinely nothing installed is reported as Absent.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task No_licence_installed_at_all_is_reported_as_Absent_not_Refused(string? rawLicenceText)
    {
        var verifier = MakeVerifier(FixedReferenceInstant);

        var status = await verifier.VerifyAsync(rawLicenceText);

        Assert.IsType<LicenceStatus.Absent>(status);
    }

    // ---- Malformed shapes that are not "not JSON at all" but still can't be a licence --------

    /// <summary>An envelope with no <c>signature</c> field at all is refused, not treated as valid.</summary>
    [Fact]
    public async Task A_licence_envelope_missing_the_signature_field_is_refused_as_Malformed()
    {
        var verifier = MakeVerifier(FixedReferenceInstant);

        var status = await verifier.VerifyAsync("""{"payload":{"id":"x"}}""");

        var refused = Assert.IsType<LicenceStatus.Refused>(status);
        Assert.Equal(LicenceRefusalReason.Malformed, refused.Reason);
    }

    /// <summary>A non-base64 <c>signature</c> value never reaches the signature verifier at all.</summary>
    [Fact]
    public async Task A_licence_envelope_whose_signature_is_not_base64_is_refused_as_Malformed()
    {
        var verifier = MakeVerifier(FixedReferenceInstant);

        var status = await verifier.VerifyAsync("""{"payload":{"id":"x"},"signature":"not-base64!!"}""");

        var refused = Assert.IsType<LicenceStatus.Refused>(status);
        Assert.Equal(LicenceRefusalReason.Malformed, refused.Reason);
    }

    /// <summary>A validly signed payload that is still missing a required field is Malformed, never Valid.</summary>
    [Fact]
    public async Task A_correctly_signed_payload_missing_the_expiry_field_is_refused_as_Malformed()
    {
        // Signed correctly over exactly these (incomplete) bytes, so a Malformed verdict here can
        // only be explained by the missing field — never by the signature.
        const string payloadJson = """{"id":"lic-001","product":"maran","tier":"included","modules":["sites"]}""";
        var envelope = LicenceEnvelopeBuilder.BuildRaw(TestKeys.RealPrivateKeyHex, payloadJson);
        var verifier = MakeVerifier(FixedReferenceInstant);

        var status = await verifier.VerifyAsync(envelope);

        var refused = Assert.IsType<LicenceStatus.Refused>(status);
        Assert.Equal(LicenceRefusalReason.Malformed, refused.Reason);
    }

    // ---- Structural §229 property: never throws ----------------------------------------------

    /// <summary>
    /// The structural half of the §229 mutant (row 4 of the table): whatever garbage is handed in,
    /// <c>VerifyAsync</c> answers a <see cref="LicenceStatus"/> and never lets an exception escape.
    /// This is a unit-level approximation of that row — the table's own full statement (that no
    /// FUTURE caller can turn this into a thrown exception on a Host-composition path) needs an
    /// architecture-level test against real caller code that does not exist yet in this slice; see
    /// this module's log entry for why that half is not attempted here.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"payload":null,"signature":"AA=="}""")]
    [InlineData("""{"payload":{"id":1,"product":2,"tier":3,"modules":"x","expiry":true},"signature":"AA=="}""")]
    [InlineData("""{"payload":{"id":"x","product":"maran","tier":"t","modules":[1,2],"expiry":"not-a-date"},"signature":"AA=="}""")]
    public async Task Verifying_an_absent_or_malformed_licence_never_throws_and_never_prevents_the_host_from_composing(
        string? rawLicenceText)
    {
        var verifier = MakeVerifier(FixedReferenceInstant);

        var status = await verifier.VerifyAsync(rawLicenceText);

        Assert.True(status is LicenceStatus.Absent or LicenceStatus.Refused);
    }

    /// <summary>Inverse control for the never-throws property: a genuinely valid licence still verifies normally.</summary>
    [Fact]
    public async Task Verifying_a_valid_licence_returns_Valid_and_the_host_composes_normally()
    {
        var envelope = LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex);
        var verifier = MakeVerifier(FixedReferenceInstant);

        var status = await verifier.VerifyAsync(envelope);

        var valid = Assert.IsType<LicenceStatus.Valid>(status);
        Assert.Equal("lic-001", valid.Licence.Id.Value);
        Assert.Equal(["databases", "sites"], valid.Licence.Modules);
    }

    // ---- Server binding ----------------------------------------------------------------------

    /// <summary>
    /// Kills the "binding is ignored" mutant: a licence issued for one server is refused on another.
    /// </summary>
    /// <remarks>
    /// This is the test the whole commercial claim rests on. Everything else about binding — that
    /// an unbound licence still works, that an unreadable identity refuses — is a qualification of
    /// this one sentence: a paid licence copied to a second machine does not work there.
    /// </remarks>
    [Fact]
    public async Task A_licence_issued_for_another_server_is_refused_as_FingerprintMismatch()
    {
        var envelope = LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, server: AnotherHostMachineId);
        var verifier = MakeVerifier(FixedReferenceInstant, ThisHostMachineId);

        var status = await verifier.VerifyAsync(envelope);

        var refused = Assert.IsType<LicenceStatus.Refused>(status);
        Assert.Equal(LicenceRefusalReason.FingerprintMismatch, refused.Reason);
    }

    /// <summary>A licence issued for THIS server verifies here.</summary>
    /// <remarks>
    /// The other half of the mutant above, and not a formality: a binding that refused everywhere
    /// would kill the mutant just as loudly while making the product unusable. One test cannot tell
    /// "compares correctly" from "always refuses"; these two together can.
    /// </remarks>
    [Fact]
    public async Task A_licence_issued_for_this_server_is_valid_here()
    {
        var envelope = LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, server: ThisHostMachineId);
        var verifier = MakeVerifier(FixedReferenceInstant, ThisHostMachineId);

        var status = await verifier.VerifyAsync(envelope);

        Assert.IsType<LicenceStatus.Valid>(status);
    }

    /// <summary>A licence naming no server runs on a host whose identity is unknown.</summary>
    /// <remarks>
    /// Guards the direction that would be easy to over-tighten: making every unreadable identity
    /// refuse, binding or no binding, would break trial licences on every container without a
    /// machine-id.
    /// </remarks>
    [Fact]
    public async Task A_licence_naming_no_server_is_valid_even_when_this_host_has_no_identity()
    {
        var envelope = LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex);
        var verifier = MakeVerifier(FixedReferenceInstant, hostMachineId: null);

        var status = await verifier.VerifyAsync(envelope);

        Assert.IsType<LicenceStatus.Valid>(status);
    }

    /// <summary>A BOUND licence is refused when this host's identity cannot be read at all.</summary>
    /// <remarks>
    /// The deliberate, costly direction: an honest customer whose agent is down sees a refusal. The
    /// alternative hands anyone who can stop the agent a licence valid on every machine they own,
    /// which is the attack binding exists to prevent. If this test is ever "fixed" to expect Valid,
    /// read LicenceServerBindingPolicy's remarks first — the argument is there, not here.
    /// </remarks>
    [Fact]
    public async Task A_bound_licence_is_refused_when_this_hosts_identity_cannot_be_read()
    {
        var envelope = LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, server: ThisHostMachineId);
        var verifier = MakeVerifier(FixedReferenceInstant, hostMachineId: null);

        var status = await verifier.VerifyAsync(envelope);

        var refused = Assert.IsType<LicenceStatus.Refused>(status);
        Assert.Equal(LicenceRefusalReason.FingerprintMismatch, refused.Reason);
    }

    /// <summary>A forged licence naming THIS server reads as a forgery, not as a mismatch.</summary>
    /// <remarks>
    /// Kills the "check the binding before the signature" mutant. The order is what an operator is
    /// told: a licence someone fabricated for this very machine must say the signature is wrong,
    /// because that is the actionable fact. A mismatch verdict would send them looking for the
    /// wrong problem.
    /// </remarks>
    [Fact]
    public async Task A_forged_licence_naming_this_server_is_refused_as_SignatureInvalid()
    {
        var envelope = LicenceEnvelopeBuilder.Build(TestKeys.OtherPrivateKeyHex, server: ThisHostMachineId);
        var verifier = MakeVerifier(FixedReferenceInstant, ThisHostMachineId);

        var status = await verifier.VerifyAsync(envelope);

        var refused = Assert.IsType<LicenceStatus.Refused>(status);
        Assert.Equal(LicenceRefusalReason.SignatureInvalid, refused.Reason);
    }

    /// <summary>A `server` claim that is present but not a string is malformed, not unbound.</summary>
    /// <remarks>
    /// The quiet failure this guards: reading a mistyped claim as "no binding" would turn an
    /// issuer's typo into a licence valid on every machine on earth.
    /// </remarks>
    [Fact]
    public async Task A_server_claim_that_is_not_a_string_is_refused_as_Malformed()
    {
        var envelope = LicenceEnvelopeBuilder.BuildRaw(
            TestKeys.RealPrivateKeyHex,
            """{"id":"lic-001","product":"maran","tier":"included","modules":["sites"],"expiry":"2026-01-31T00:00:00.0000000+00:00","server":42}""");
        var verifier = MakeVerifier(FixedReferenceInstant, ThisHostMachineId);

        var status = await verifier.VerifyAsync(envelope);

        var refused = Assert.IsType<LicenceStatus.Refused>(status);
        Assert.Equal(LicenceRefusalReason.Malformed, refused.Reason);
    }

    /// <summary>The machine-id comparison ignores case, because case difference is transcription.</summary>
    [Fact]
    public async Task A_licence_naming_this_server_in_upper_case_is_still_valid_here()
    {
        var envelope = LicenceEnvelopeBuilder.Build(
            TestKeys.RealPrivateKeyHex,
            server: ThisHostMachineId.ToUpperInvariant());
        var verifier = MakeVerifier(FixedReferenceInstant, ThisHostMachineId);

        var status = await verifier.VerifyAsync(envelope);

        Assert.IsType<LicenceStatus.Valid>(status);
    }
}
