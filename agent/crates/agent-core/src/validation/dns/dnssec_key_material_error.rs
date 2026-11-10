//! Why a DNSSEC signing key was refused.

/// Reasons [`super::dnssec_key_material::DnssecKeyMaterial::parse`] refuses a key.
///
/// No variant carries a byte of either half of the key, or the text it was parsed
/// from: an error is logged and travels to the panel, and the private half must
/// not be in either place (rules/security.md item 8).
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum DnssecKeyMaterialError {
    /// The public half contains whitespace or a control character.
    #[error("the public key contains whitespace or a control character")]
    PublicKeyControlCharacter,

    /// The private half contains whitespace or a control character.
    #[error("the private key contains whitespace or a control character")]
    PrivateKeyControlCharacter,

    /// The public half is not canonical standard base64 with padding.
    #[error("the public key is not standard base64")]
    PublicKeyNotBase64,

    /// The private half is not canonical standard base64 with padding.
    #[error("the private key is not standard base64")]
    PrivateKeyNotBase64,

    /// The public half does not decode to exactly 64 bytes (`x || y`).
    #[error("the public key is not 64 bytes")]
    PublicKeyWrongLength,

    /// The private half does not decode to exactly 32 bytes (the scalar).
    #[error("the private key is not 32 bytes")]
    PrivateKeyWrongLength,

    /// The flags field is not 257, the key-signing and zone-key bits that a
    /// combined signing key carries.
    #[error("the key flags are not 257")]
    UnsupportedFlags,

    /// The key tag the caller stated is not the one computed from the public key.
    #[error("the key tag does not match the public key")]
    KeyTagMismatch,
}
