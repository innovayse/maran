namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>
/// The keypairs this test project signs fixtures with. The public half of
/// <see cref="RealPrivateKeyHex"/>'s keypair is the exact value embedded in
/// <c>Ed25519LicenceSignatureVerifier.EmbeddedPublicKey</c> — generated once for this pass (see that
/// type's own remarks: no real Innovayse keypair exists to test against), and
/// <see cref="OtherPrivateKeyHex"/> is an unrelated keypair used only to prove a licence signed with
/// the WRONG key is refused.
/// </summary>
internal static class TestKeys
{
    /// <summary>
    /// The private half of the keypair whose public half is embedded in
    /// <c>Ed25519LicenceSignatureVerifier</c>. Test-only; never a production secret.
    /// </summary>
    internal const string RealPrivateKeyHex = "BE9998393514EDE78E57344B69C2F51B4B4CEA3883696F2EDB178A796512A727";

    /// <summary>An unrelated keypair's private half, used to sign a licence the embedded key must reject.</summary>
    internal const string OtherPrivateKeyHex = "C46777FD2B7B516C68A6649CC1F7E1538E840F5462AFB5A3737E676DF2545F50";
}
