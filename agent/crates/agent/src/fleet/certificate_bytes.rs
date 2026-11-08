//! The DER bytes of the one certificate a PEM file holds.

use std::path::Path;

use base64::Engine as _;
use base64::engine::general_purpose::STANDARD;

use super::fleet_identity_error::FleetIdentityError;

/// The first line of a PEM certificate.
const BEGIN_MARKER: &str = "-----BEGIN CERTIFICATE-----";

/// The last line of a PEM certificate.
const END_MARKER: &str = "-----END CERTIFICATE-----";

/// The most DER bytes a fleet certificate may have. A self-signed P-256 certificate is about
/// 400; this leaves room for long names and refuses a file that is something else.
const MAX_DER_BYTES: usize = 16 * 1024;

/// The tag byte every X.509 certificate starts with: a DER `SEQUENCE`.
const DER_SEQUENCE_TAG: u8 = 0x30;

/// Decodes `pem` as EXACTLY one PEM certificate and returns its DER bytes.
///
/// Strict on purpose, because the bytes are what a fingerprint is taken over and what a trust
/// decision rests on: the text must be one `CERTIFICATE` block and nothing else. A file with a
/// second block, text before or after it, a different label, or a body that is not canonical
/// base64 is refused instead of having its first certificate picked out, since "the first
/// certificate in the file" is not what an operator who put two there meant.
///
/// What this does NOT do is parse the X.509 structure: it proves the bytes are a DER sequence of
/// a sane size, no more. The structure is read, and a malformed certificate refused, when the
/// TLS configuration is built from the same file.
///
/// `path` is only for the error text.
///
/// # Errors
///
/// Returns [`FleetIdentityError::CertificateMalformed`] when the text is not exactly one
/// `-----BEGIN CERTIFICATE-----` block, when its body is not base64, is empty or too large, or
/// does not decode to a DER sequence.
pub(super) fn certificate_bytes(path: &Path, pem: &str) -> Result<Vec<u8>, FleetIdentityError> {
    let malformed = |reason: &'static str| FleetIdentityError::CertificateMalformed {
        path: path.to_path_buf(),
        reason,
    };

    let mut lines = pem.trim().lines().map(str::trim_end);

    if lines.next() != Some(BEGIN_MARKER) {
        return Err(malformed("it does not start with a BEGIN CERTIFICATE line"));
    }

    let mut body = String::new();
    let mut ended = false;
    for line in lines.by_ref() {
        if line == END_MARKER {
            ended = true;
            break;
        }
        if line.starts_with("-----") {
            return Err(malformed("it holds more than one PEM block"));
        }
        body.push_str(line);
    }

    if !ended {
        return Err(malformed("it has no END CERTIFICATE line"));
    }
    if let Some(extra) = lines.next() {
        return Err(malformed(if extra.starts_with("-----BEGIN") {
            "it holds more than one PEM block"
        } else {
            "it has text after the END CERTIFICATE line"
        }));
    }
    if body.is_empty() {
        return Err(malformed("the certificate body is empty"));
    }

    let der = STANDARD
        .decode(body.as_bytes())
        .map_err(|_| malformed("the body is not base64"))?;

    if der.len() > MAX_DER_BYTES {
        return Err(malformed("the certificate is too large"));
    }
    if der.first() != Some(&DER_SEQUENCE_TAG) {
        return Err(malformed("the body is not a DER certificate"));
    }

    Ok(der)
}

#[cfg(test)]
#[path = "../tests/fleet/certificate_bytes_tests.rs"]
mod tests;
