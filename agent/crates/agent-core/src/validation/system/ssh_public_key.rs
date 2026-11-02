//! An OpenSSH public key, accepted only in the shape an `authorized_keys` line
//! may safely be built from.

use base64::Engine as _;
use base64::engine::general_purpose::STANDARD;

use super::ssh_public_key_error::SshPublicKeyError;

/// The smallest RSA modulus accepted, in bits.
const RSA_MIN_BITS: usize = 2048;

/// The largest RSA modulus accepted, in bits; OpenSSH's own ceiling.
const RSA_MAX_BITS: usize = 16384;

/// The length of an Ed25519 public key, in bytes.
const ED25519_KEY_LENGTH: usize = 32;

/// The tag byte of an uncompressed elliptic-curve point.
const UNCOMPRESSED_POINT: u8 = 0x04;

/// The accepted algorithm names, and nothing else. `ssh-dss` is absent on
/// purpose: OpenSSH itself has disabled it by default since 7.0.
const ACCEPTED_ALGORITHMS: &[&str] = &[
    "ssh-ed25519",
    "sk-ssh-ed25519@openssh.com",
    "ecdsa-sha2-nistp256",
    "ecdsa-sha2-nistp384",
    "ecdsa-sha2-nistp521",
    "sk-ecdsa-sha2-nistp256@openssh.com",
    "ssh-rsa",
];

/// A public key that may become one line of a root-owned `authorized_keys`
/// file.
///
/// Constructed only by [`SshPublicKey::parse`], so a value of this type is a
/// key and not merely key-shaped text: the algorithm is allow-listed, the
/// base64 field decodes, the decoded blob names the same algorithm and is a
/// well-formed key of it, and no control character exists anywhere in it.
///
/// What it deliberately does NOT keep is everything a caller could use to
/// steer sshd: there is no option field (a line that begins with one is
/// refused as an unsupported algorithm, because options are never parsed),
/// and the comment is dropped rather than escaped. [`SshPublicKey::as_line`]
/// is the whole of what is written, and the writer prefixes the one option it
/// chooses itself (rules/security.md item 4: validated, not escaped).
#[derive(Debug, Clone, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct SshPublicKey {
    /// The allow-listed algorithm name, as written in the first field.
    algorithm: &'static str,
    /// The base64 field exactly as supplied; it decoded canonically.
    data: String,
}

impl SshPublicKey {
    /// The longest key text accepted, in bytes. A 16384-bit RSA key is under
    /// 3 KiB of base64, so this bounds the file without refusing a real key.
    pub const MAX_LENGTH: usize = 8 * 1024;

    /// Parses `text` as `<algorithm> <base64> [comment]`.
    ///
    /// Surrounding spaces are ignored; the comment, when present, is dropped.
    ///
    /// # Errors
    ///
    /// - [`SshPublicKeyError::Empty`] when nothing but spaces was supplied.
    /// - [`SshPublicKeyError::TooLong`] when `text` exceeds
    ///   [`Self::MAX_LENGTH`] bytes.
    /// - [`SshPublicKeyError::ControlCharacter`] when any control character
    ///   appears, checked before the text is split.
    /// - [`SshPublicKeyError::UnsupportedAlgorithm`] when the first field is not
    ///   an accepted algorithm — including a leading option.
    /// - [`SshPublicKeyError::InvalidEncoding`] when the second field is absent
    ///   or is not canonical base64.
    /// - [`SshPublicKeyError::AlgorithmMismatch`] when the decoded key names
    ///   another algorithm.
    /// - [`SshPublicKeyError::Malformed`] when the decoded key is not a
    ///   well-formed key of its algorithm.
    /// - [`SshPublicKeyError::RsaSizeRefused`] when an RSA modulus is outside
    ///   2048 to 16384 bits.
    pub fn parse(text: &str) -> Result<Self, SshPublicKeyError> {
        if text.len() > Self::MAX_LENGTH {
            return Err(SshPublicKeyError::TooLong {
                length: text.len(),
                limit: Self::MAX_LENGTH,
            });
        }
        // Before any split: a newline must never get the chance to be read as
        // a field boundary, or as the start of a second line.
        if text.chars().any(char::is_control) {
            return Err(SshPublicKeyError::ControlCharacter);
        }

        let mut fields = text.split(' ').filter(|field| !field.is_empty());
        let declared = fields.next().ok_or(SshPublicKeyError::Empty)?;
        let algorithm = ACCEPTED_ALGORITHMS
            .iter()
            .copied()
            .find(|accepted| *accepted == declared)
            .ok_or(SshPublicKeyError::UnsupportedAlgorithm)?;
        let data = fields.next().ok_or(SshPublicKeyError::InvalidEncoding)?;
        let blob = STANDARD
            .decode(data)
            .map_err(|_| SshPublicKeyError::InvalidEncoding)?;

        check_blob(algorithm, &blob)?;

        Ok(Self {
            algorithm,
            data: data.to_owned(),
        })
    }

    /// The algorithm name.
    pub fn algorithm(&self) -> &str {
        self.algorithm
    }

    /// `<algorithm> <base64>`: the key with no option and no comment, ready to
    /// follow the option its writer chooses.
    pub fn as_line(&self) -> String {
        format!("{} {}", self.algorithm, self.data)
    }
}

/// Checks that `blob` is a well-formed key of `algorithm`.
///
/// # Errors
///
/// [`SshPublicKeyError::AlgorithmMismatch`], [`SshPublicKeyError::Malformed`]
/// and [`SshPublicKeyError::RsaSizeRefused`], as [`SshPublicKey::parse`]
/// documents them.
fn check_blob(algorithm: &str, blob: &[u8]) -> Result<(), SshPublicKeyError> {
    let mut reader = WireReader { rest: blob };
    if reader.string()? != algorithm.as_bytes() {
        return Err(SshPublicKeyError::AlgorithmMismatch);
    }

    match algorithm {
        "ssh-ed25519" => exact_length(reader.string()?, ED25519_KEY_LENGTH)?,
        "sk-ssh-ed25519@openssh.com" => {
            exact_length(reader.string()?, ED25519_KEY_LENGTH)?;
            reader.string()?;
        }
        "ecdsa-sha2-nistp256" => curve_point(&mut reader, "nistp256", 65)?,
        "ecdsa-sha2-nistp384" => curve_point(&mut reader, "nistp384", 97)?,
        "ecdsa-sha2-nistp521" => curve_point(&mut reader, "nistp521", 133)?,
        "sk-ecdsa-sha2-nistp256@openssh.com" => {
            curve_point(&mut reader, "nistp256", 65)?;
            reader.string()?;
        }
        _ => rsa_modulus(&mut reader)?,
    }

    // Trailing bytes are a second structure smuggled behind a valid one.
    if reader.rest.is_empty() {
        Ok(())
    } else {
        Err(SshPublicKeyError::Malformed)
    }
}

/// Requires `field` to be exactly `length` bytes.
///
/// # Errors
///
/// [`SshPublicKeyError::Malformed`] on any other length.
fn exact_length(field: &[u8], length: usize) -> Result<(), SshPublicKeyError> {
    if field.len() == length {
        Ok(())
    } else {
        Err(SshPublicKeyError::Malformed)
    }
}

/// Reads an ECDSA curve name and point, requiring the named curve and an
/// uncompressed point of `point_length` bytes.
///
/// # Errors
///
/// [`SshPublicKeyError::Malformed`] on a short field, another curve, or a
/// point of the wrong length or form.
fn curve_point(
    reader: &mut WireReader<'_>,
    curve: &str,
    point_length: usize,
) -> Result<(), SshPublicKeyError> {
    if reader.string()? != curve.as_bytes() {
        return Err(SshPublicKeyError::Malformed);
    }
    let point = reader.string()?;
    exact_length(point, point_length)?;
    if point.first() == Some(&UNCOMPRESSED_POINT) {
        Ok(())
    } else {
        Err(SshPublicKeyError::Malformed)
    }
}

/// Reads an RSA exponent and modulus and bounds the modulus size.
///
/// # Errors
///
/// [`SshPublicKeyError::Malformed`] on a short field or an empty exponent, and
/// [`SshPublicKeyError::RsaSizeRefused`] on a modulus outside the bounds.
fn rsa_modulus(reader: &mut WireReader<'_>) -> Result<(), SshPublicKeyError> {
    let exponent = significant(reader.string()?);
    if exponent.is_empty() {
        return Err(SshPublicKeyError::Malformed);
    }

    let modulus = significant(reader.string()?);
    let bits = match modulus.first() {
        Some(top) => (modulus.len() - 1) * 8 + (8 - top.leading_zeros() as usize),
        None => 0,
    };
    if (RSA_MIN_BITS..=RSA_MAX_BITS).contains(&bits) {
        Ok(())
    } else {
        Err(SshPublicKeyError::RsaSizeRefused { bits })
    }
}

/// An `mpint` without its leading zero bytes.
fn significant(mpint: &[u8]) -> &[u8] {
    let start = mpint
        .iter()
        .position(|byte| *byte != 0)
        .unwrap_or(mpint.len());
    &mpint[start..]
}

/// A cursor over the SSH wire format (RFC 4251 §5): length-prefixed strings.
struct WireReader<'a> {
    /// What has not been read yet.
    rest: &'a [u8],
}

impl<'a> WireReader<'a> {
    /// Reads one `string`: a big-endian `u32` length and that many bytes.
    ///
    /// # Errors
    ///
    /// [`SshPublicKeyError::Malformed`] when the length or the bytes it
    /// promises are not there.
    fn string(&mut self) -> Result<&'a [u8], SshPublicKeyError> {
        let (length, rest) = self
            .rest
            .split_first_chunk::<4>()
            .ok_or(SshPublicKeyError::Malformed)?;
        let length = usize::try_from(u32::from_be_bytes(*length))
            .map_err(|_| SshPublicKeyError::Malformed)?;
        if rest.len() < length {
            return Err(SshPublicKeyError::Malformed);
        }
        let (field, rest) = rest.split_at(length);
        self.rest = rest;

        Ok(field)
    }
}

#[cfg(test)]
#[path = "../../tests/validation/system/ssh_public_key_tests.rs"]
mod tests;
