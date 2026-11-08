//! The private key a domain's outgoing mail is signed with.

use base64::Engine as _;
use base64::engine::general_purpose::STANDARD;

use super::der_reader::DerReader;
use super::dkim_private_key_error::DkimPrivateKeyError;
use crate::secret_string::SecretString;

/// The longest document accepted, in bytes. A 4096-bit PKCS#8 key is about 3.3
/// KiB of PEM; the ceiling bounds the file and refuses nothing real.
pub const MAXIMUM_LENGTH: usize = 8 * 1024;

/// The smallest RSA modulus accepted, in bits.
const MINIMUM_BITS: usize = 2048;

/// The largest RSA modulus accepted, in bits. Beyond it the public key no
/// longer fits one DNS TXT record comfortably, which is what makes receivers
/// ignore a signature.
const MAXIMUM_BITS: usize = 4096;

/// The opening line of the document.
const BEGIN: &str = "-----BEGIN PRIVATE KEY-----\n";

/// The closing line of the document, without its line feed.
const END: &str = "\n-----END PRIVATE KEY-----";

/// The longest body line accepted. PEM writes 64; some writers use 76.
const MAXIMUM_LINE: usize = 76;

/// The encoded OBJECT IDENTIFIER `1.2.840.113549.1.1.1`, `rsaEncryption`.
const RSA_ENCRYPTION: &[u8] = &[0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01];

/// A DKIM signing key: an RSA private key of 2048 to 4096 bits as a PKCS#8 PEM
/// document, proven to be that structure before it is written.
///
/// Constructed only by [`DkimPrivateKey::parse`], so a value of this type is
/// a key-shaped document and not merely text that starts with a banner: the
/// armour lines are exact, the body decodes as canonical base64, and the
/// decoded bytes are a PKCS#8 `PrivateKeyInfo` whose algorithm is RSA and whose
/// modulus has an accepted size. A document that is none of those would be
/// written root-owned into the signing daemon's directory and fail there, far
/// from the call that sent it.
///
/// What it does NOT prove: that the numbers inside are a consistent RSA key.
/// The agent never signs with the key and has no use for its arithmetic; the
/// signing daemon rejects an inconsistent one when it loads it.
///
/// The key is a credential at rest. It is held in a [`SecretString`], so
/// `Debug` and `Display` print `«redacted»`, and [`DkimPrivateKey::expose`] is
/// the only reader, named to be greppable.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DkimPrivateKey(SecretString);

impl DkimPrivateKey {
    /// Parses `pem` as a PKCS#8 RSA private key.
    ///
    /// One trailing line feed is accepted and the stored form always ends with
    /// exactly one.
    ///
    /// # Errors
    ///
    /// - [`DkimPrivateKeyError::TooLong`] beyond [`MAXIMUM_LENGTH`] bytes.
    /// - [`DkimPrivateKeyError::ControlCharacter`] for a control character
    ///   other than `\n`.
    /// - [`DkimPrivateKeyError::NotPkcs8Pem`] when the armour lines are not
    ///   exactly `BEGIN PRIVATE KEY` and `END PRIVATE KEY`, or the body has an
    ///   empty or over-long line.
    /// - [`DkimPrivateKeyError::InvalidEncoding`] when the body is not
    ///   canonical base64.
    /// - [`DkimPrivateKeyError::NotAnRsaKey`] when the algorithm is not RSA.
    /// - [`DkimPrivateKeyError::Malformed`] when the decoded bytes are not
    ///   the structure of a PKCS#8 RSA key.
    /// - [`DkimPrivateKeyError::KeySizeRefused`] when the modulus is outside
    ///   2048 to 4096 bits.
    pub fn parse(pem: &str) -> Result<Self, DkimPrivateKeyError> {
        if pem.len() > MAXIMUM_LENGTH {
            return Err(DkimPrivateKeyError::TooLong {
                maximum: MAXIMUM_LENGTH,
            });
        }

        if pem.chars().any(|c| c.is_control() && c != '\n') {
            return Err(DkimPrivateKeyError::ControlCharacter);
        }

        let document = pem.strip_suffix('\n').unwrap_or(pem);
        let body = document
            .strip_prefix(BEGIN)
            .and_then(|rest| rest.strip_suffix(END))
            .ok_or(DkimPrivateKeyError::NotPkcs8Pem)?;

        if body.is_empty()
            || body
                .split('\n')
                .any(|line| line.is_empty() || line.len() > MAXIMUM_LINE)
        {
            return Err(DkimPrivateKeyError::NotPkcs8Pem);
        }

        let joined: String = body.split('\n').collect();
        let der = STANDARD
            .decode(joined.as_bytes())
            .map_err(|_| DkimPrivateKeyError::InvalidEncoding)?;

        check_pkcs8_rsa(&der)?;

        Ok(Self(SecretString::new(format!("{document}\n"))))
    }

    /// The key as a PEM document, for the one place that writes it into the
    /// signing daemon's directory.
    ///
    /// Named after what it does, and the only reader: a search for it lists
    /// every place the key leaves its wrapper.
    #[must_use]
    pub fn expose(&self) -> &str {
        self.0.expose()
    }
}

/// Checks that `der` is a PKCS#8 `PrivateKeyInfo` for an RSA key of an
/// accepted size:
///
/// ```text
/// SEQUENCE {
///   INTEGER 0
///   SEQUENCE { OBJECT IDENTIFIER rsaEncryption, NULL }
///   OCTET STRING { SEQUENCE { INTEGER 0, INTEGER modulus, ... } }
/// }
/// ```
fn check_pkcs8_rsa(der: &[u8]) -> Result<(), DkimPrivateKeyError> {
    let malformed = || DkimPrivateKeyError::Malformed;

    let mut outer = DerReader::new(der);
    let info = outer.read(DerReader::SEQUENCE).ok_or_else(malformed)?;
    if !outer.is_empty() {
        return Err(malformed());
    }

    let mut info = DerReader::new(info);
    if info.read(DerReader::INTEGER) != Some(&[0u8][..]) {
        return Err(malformed());
    }

    let algorithm = info.read(DerReader::SEQUENCE).ok_or_else(malformed)?;
    let mut algorithm = DerReader::new(algorithm);
    let object = algorithm
        .read(DerReader::OBJECT_IDENTIFIER)
        .ok_or_else(malformed)?;
    if object != RSA_ENCRYPTION {
        return Err(DkimPrivateKeyError::NotAnRsaKey);
    }
    if algorithm.read(DerReader::NULL) != Some(&[][..]) || !algorithm.is_empty() {
        return Err(malformed());
    }

    let key = info.read(DerReader::OCTET_STRING).ok_or_else(malformed)?;
    if !info.is_empty() {
        return Err(malformed());
    }

    let mut key = DerReader::new(key);
    let rsa = key.read(DerReader::SEQUENCE).ok_or_else(malformed)?;
    if !key.is_empty() {
        return Err(malformed());
    }

    let mut rsa = DerReader::new(rsa);
    if rsa.read(DerReader::INTEGER) != Some(&[0u8][..]) {
        return Err(malformed());
    }
    let modulus = rsa.read(DerReader::INTEGER).ok_or_else(malformed)?;

    let bits = modulus_bits(modulus).ok_or_else(malformed)?;
    if !(MINIMUM_BITS..=MAXIMUM_BITS).contains(&bits) {
        return Err(DkimPrivateKeyError::KeySizeRefused {
            bits,
            minimum: MINIMUM_BITS,
            maximum: MAXIMUM_BITS,
        });
    }

    Ok(())
}

/// The bit length of a DER INTEGER holding a positive modulus, or `None` when
/// the integer is empty, negative or not minimally encoded.
///
/// A positive integer whose top bit is set carries one leading zero byte to
/// keep it positive; that byte is not part of the modulus. Any other leading
/// zero, or a set sign bit, is not a modulus.
fn modulus_bits(integer: &[u8]) -> Option<usize> {
    let (&first, rest) = integer.split_first()?;
    let (top, length) = if first == 0 {
        let (&next, _) = rest.split_first()?;
        if next & 0x80 == 0 {
            return None;
        }
        (next, rest.len())
    } else {
        if first & 0x80 != 0 {
            return None;
        }
        (first, integer.len())
    };

    let leading = usize::try_from(top.leading_zeros()).ok()?;
    (length * 8).checked_sub(leading)
}

#[cfg(test)]
#[path = "../../tests/validation/mail/dkim_private_key_tests.rs"]
mod tests;
