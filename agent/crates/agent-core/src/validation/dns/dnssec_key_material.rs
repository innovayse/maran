//! A DNSSEC signing key the agent installs: ECDSA P-256, one combined signing key.

use base64::Engine as _;
use base64::engine::general_purpose::STANDARD;

use crate::secret_string::SecretString;
use crate::validation::dns::dnssec_key_material_error::DnssecKeyMaterialError;

/// The longest text either half may have before it is decoded, in characters.
/// The real texts are 88 and 44; the bound only keeps a hostile text from being
/// decoded at all.
const MAX_TEXT_LENGTH: usize = 128;

/// Bytes of the public key: the P-256 point as `x || y`, 32 bytes each.
const PUBLIC_KEY_BYTES: usize = 64;

/// Bytes of the private key: the P-256 scalar.
const PRIVATE_KEY_BYTES: usize = 32;

/// The DNSKEY record bytes before the public key: flags (2), protocol (1) and
/// algorithm (1).
const RDATA_HEADER_BYTES: usize = 4;

/// A validated ECDSA P-256 signing key: the public half in the clear, the private
/// half behind a [`SecretString`], and the RFC 4034 key tag that names them.
///
/// **What is fixed, and so unrepresentable as anything else.** The algorithm is
/// [`Self::ALGORITHM`] (13, ECDSAP256SHA256) and not a parameter: a request cannot
/// ask for another. The flags must be [`Self::FLAGS`] (257, a zone key with the
/// secure-entry-point bit: one combined signing key, no separate key-signing
/// key). The public key must decode to exactly 64 bytes and the private key to
/// exactly 32. The key tag is never taken from the caller: it is recomputed from
/// the DNSKEY record bytes (RFC 4034 appendix B) and the caller's value must equal
/// it, so the `K<zone>.+013+<tag>` file name that
/// [`DnssecKeyFileName`](crate::validation::dns::dnssec_key_file_name::DnssecKeyFileName)
/// derives is built from a number the agent computed.
///
/// Both halves are checked as text first: whitespace and control characters are
/// refused outright (rules/security.md item 4, because the private half is
/// written into a line-oriented key file), then the text must be canonical
/// standard base64 with padding.
///
/// **The private half never leaves through `Debug`.** `Debug` is derived and the
/// field is a [`SecretString`], which prints `«redacted»`. There is deliberately
/// no `Display`, no `PartialEq` (a variable-time comparison of a key has no
/// caller), no `serde` implementation and no accessor but
/// [`Self::expose_private_key_base64`], named like [`SecretString::expose`] so one
/// search lists every place the key leaves its wrapper.
#[derive(Debug, Clone)]
pub struct DnssecKeyMaterial {
    /// The public key as supplied, which decoded canonically to 64 bytes.
    public_key_base64: String,
    /// The private scalar, base64, which decoded to 32 bytes.
    private_key: SecretString,
    /// The key tag, recomputed and equal to the caller's.
    key_tag: u16,
}

impl DnssecKeyMaterial {
    /// The flags of the key: zone key (256) plus secure entry point (1).
    pub const FLAGS: u16 = 257;

    /// The DNSKEY protocol field, always 3.
    pub const PROTOCOL: u8 = 3;

    /// The DNSSEC algorithm number: 13, ECDSAP256SHA256 (RFC 6605).
    pub const ALGORITHM: u8 = 13;

    /// Parses the key a request carries.
    ///
    /// `flags` and `key_tag` are the wire's unsigned 32-bit fields, taken as they
    /// came so an out-of-range value is a refusal here and not a truncation at
    /// the caller.
    ///
    /// # Errors
    ///
    /// - [`DnssecKeyMaterialError::PublicKeyControlCharacter`] and
    ///   [`DnssecKeyMaterialError::PrivateKeyControlCharacter`] for whitespace or a
    ///   control character in the corresponding half.
    /// - [`DnssecKeyMaterialError::PublicKeyNotBase64`] and
    ///   [`DnssecKeyMaterialError::PrivateKeyNotBase64`] for text that is not
    ///   canonical standard base64.
    /// - [`DnssecKeyMaterialError::PublicKeyWrongLength`] and
    ///   [`DnssecKeyMaterialError::PrivateKeyWrongLength`] when the half does not
    ///   decode to 64 and 32 bytes (or is too long to be decoded at all).
    /// - [`DnssecKeyMaterialError::UnsupportedFlags`] for flags other than 257.
    /// - [`DnssecKeyMaterialError::KeyTagMismatch`] when `key_tag` differs from
    ///   the tag computed from the public key.
    pub fn parse(
        public_key_base64: &str,
        private_key: &SecretString,
        flags: u32,
        key_tag: u32,
    ) -> Result<Self, DnssecKeyMaterialError> {
        let public_bytes = decode(
            public_key_base64,
            PUBLIC_KEY_BYTES,
            [
                DnssecKeyMaterialError::PublicKeyControlCharacter,
                DnssecKeyMaterialError::PublicKeyNotBase64,
                DnssecKeyMaterialError::PublicKeyWrongLength,
            ],
        )?;
        decode(
            private_key.expose(),
            PRIVATE_KEY_BYTES,
            [
                DnssecKeyMaterialError::PrivateKeyControlCharacter,
                DnssecKeyMaterialError::PrivateKeyNotBase64,
                DnssecKeyMaterialError::PrivateKeyWrongLength,
            ],
        )?;

        if flags != u32::from(Self::FLAGS) {
            return Err(DnssecKeyMaterialError::UnsupportedFlags);
        }

        let computed = compute_key_tag(Self::FLAGS, &public_bytes);
        if key_tag != u32::from(computed) {
            return Err(DnssecKeyMaterialError::KeyTagMismatch);
        }

        Ok(Self {
            public_key_base64: public_key_base64.to_owned(),
            private_key: private_key.clone(),
            key_tag: computed,
        })
    }

    /// The RFC 4034 appendix B key tag of the public key, computed and not
    /// supplied.
    #[must_use]
    pub fn key_tag(&self) -> u16 {
        self.key_tag
    }

    /// The public key, base64 of `x || y` (64 bytes): the text a DNSKEY record
    /// and a `.key` file carry.
    #[must_use]
    pub fn public_key_base64(&self) -> &str {
        &self.public_key_base64
    }

    /// The flags: always [`Self::FLAGS`].
    #[must_use]
    pub fn flags(&self) -> u16 {
        Self::FLAGS
    }

    /// The DNSKEY protocol field: always [`Self::PROTOCOL`].
    #[must_use]
    pub fn protocol(&self) -> u8 {
        Self::PROTOCOL
    }

    /// The algorithm: always [`Self::ALGORITHM`].
    #[must_use]
    pub fn algorithm(&self) -> u8 {
        Self::ALGORITHM
    }

    /// The private scalar as base64 (32 bytes decoded), for the one writer of the
    /// `.private` key file.
    ///
    /// The only reader of the private half, named `expose` so that every place the
    /// key leaves its wrapper is found with one grep (`expose_private_key_base64`).
    /// Its result goes into a file written under `safe_write` and nowhere else:
    /// never a log line, an error, an audit line or a command line.
    #[must_use]
    pub fn expose_private_key_base64(&self) -> &str {
        self.private_key.expose()
    }
}

/// Checks one half's text and decodes it to exactly `expected` bytes.
///
/// `faults` are, in order, the control-character, not-base64 and wrong-length
/// errors of the half being checked, so one body serves both halves without the
/// error naming which one by a branch.
fn decode(
    text: &str,
    expected: usize,
    faults: [DnssecKeyMaterialError; 3],
) -> Result<Vec<u8>, DnssecKeyMaterialError> {
    let [control, not_base64, wrong_length] = faults;

    if text.chars().any(|c| c.is_control() || c.is_whitespace()) {
        return Err(control);
    }
    if text.len() > MAX_TEXT_LENGTH {
        return Err(wrong_length);
    }

    let bytes = STANDARD.decode(text).map_err(|_| not_base64)?;
    if bytes.len() != expected {
        return Err(wrong_length);
    }

    Ok(bytes)
}

/// The key tag of RFC 4034 appendix B over the DNSKEY RDATA: flags, protocol 3,
/// algorithm 13 and the public key.
///
/// The sum of the RDATA read as big-endian 16-bit words (an odd final byte is the
/// high half of a word), with the carries above bit 15 folded in once.
fn compute_key_tag(flags: u16, public_key: &[u8]) -> u16 {
    let mut rdata = Vec::with_capacity(RDATA_HEADER_BYTES + public_key.len());
    rdata.extend_from_slice(&flags.to_be_bytes());
    rdata.push(DnssecKeyMaterial::PROTOCOL);
    rdata.push(DnssecKeyMaterial::ALGORITHM);
    rdata.extend_from_slice(public_key);

    let mut accumulator: u32 = 0;
    for (index, byte) in rdata.iter().enumerate() {
        if index % 2 == 0 {
            accumulator += u32::from(*byte) << 8;
        } else {
            accumulator += u32::from(*byte);
        }
    }
    accumulator += (accumulator >> 16) & 0xFFFF;

    // The low two bytes of the big-endian sum: the mask in the RFC, without a cast.
    let [_, _, high, low] = accumulator.to_be_bytes();
    u16::from_be_bytes([high, low])
}

#[cfg(test)]
#[path = "../../tests/validation/dns/dnssec_key_material_tests.rs"]
mod tests;
