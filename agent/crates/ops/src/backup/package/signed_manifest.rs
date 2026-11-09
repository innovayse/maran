//! A manifest together with the proof of who signed it.

/// The three byte strings a receiver needs to verify a package.
///
/// Opaque on purpose: `manifest` is the exact byte sequence that was signed and
/// MUST NOT be re-serialised by anyone on the way.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SignedManifest {
    /// The UTF-8 JSON of the [`TransferManifest`](super::transfer_manifest::TransferManifest),
    /// exactly as signed.
    pub manifest: Vec<u8>,
    /// The ECDSA P-384 / SHA-384 signature over `manifest`, ASN.1 DER.
    pub signature: Vec<u8>,
    /// The signer's public key: an uncompressed SEC1 point.
    pub public_key: Vec<u8>,
}
