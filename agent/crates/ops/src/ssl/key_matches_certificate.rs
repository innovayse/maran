//! The check that runs before anything reaches disk.

use maran_distro::DistroAdapter;

use crate::ssl::model::certificate_material::CertificateMaterial;
use crate::ssl::ssl_host::SslHost;
use crate::ssl::ssl_op_error::SslOpError;

/// Prints the public key of the certificate on standard input.
const CERTIFICATE_PUBLIC_KEY: [&str; 3] = ["x509", "-noout", "-pubkey"];

/// Prints the public key of the private key on standard input.
///
/// `-passin pass:` supplies an empty passphrase explicitly. Without it an
/// encrypted key makes openssl prompt: with no tty it fails, with one it would
/// block the agent forever, and which of those happens depends on how the daemon
/// was started rather than on the input. An encrypted key is refused
/// deterministically instead — the panel is expected to send an unencrypted one,
/// because nginx cannot read any other kind without a passphrase of its own.
const PRIVATE_KEY_PUBLIC_KEY: [&str; 4] = ["pkey", "-pubout", "-passin", "pass:"];

/// Whether `material`'s private key belongs to `material`'s certificate.
///
/// Decided by asking openssl for the public key each half implies and comparing
/// the two: a private key and the certificate issued for it derive the same
/// public key, and nothing else does. Comparing the whole
/// SubjectPublicKeyInfo rather than an RSA modulus is what makes this work
/// unchanged for EC and Ed25519 keys, which a modulus comparison could not read
/// at all.
///
/// The comparison happens in memory, before a single byte is written, because
/// this is the one failure the write protocol cannot protect a site from — a
/// mismatched pair is syntactically perfect, `nginx -t` accepts it, the reload
/// succeeds, and the site then fails at the first TLS handshake. By the time a
/// human notices, the rollback that would have saved the site has long since
/// committed, and the site is down at the exact moment it was supposed to become
/// secure.
///
/// Both PEMs go to the tools on standard input rather than as a file or an
/// argument, so the key never has a path and never appears in
/// `/proc/<pid>/cmdline`. The key's half runs through
/// [`SslHost::run_with_private_key`], whose outcome carries no stderr and whose
/// stdout can only be compared — so nothing openssl said about the key can be
/// formatted into the error this function returns.
///
/// # Errors
///
/// Returns [`SslOpError::ToolUnavailable`] when openssl cannot be run,
/// [`SslOpError::MalformedCertificate`] — carrying openssl's own words, since a
/// certificate is public — when it cannot read the certificate, and
/// [`SslOpError::MalformedPrivateKey`], which has nowhere to put a detail, when
/// it cannot read the key.
pub(crate) fn key_matches_certificate(
    host: &dyn SslHost,
    distro: &dyn DistroAdapter,
    material: &CertificateMaterial,
) -> Result<bool, SslOpError> {
    let openssl = distro.openssl_binary();

    let from_certificate =
        host.run_with_certificate(openssl, &CERTIFICATE_PUBLIC_KEY, material.certificate_pem())?;
    if from_certificate.status != 0 {
        return Err(SslOpError::MalformedCertificate {
            reason: from_certificate.stderr,
        });
    }

    let from_key =
        host.run_with_private_key(openssl, &PRIVATE_KEY_PUBLIC_KEY, material.private_key_pem())?;
    if !from_key.succeeded() {
        return Err(SslOpError::MalformedPrivateKey);
    }

    // A boolean is the only thing that crosses out of the key-fed process's
    // result. The public key the certificate implies is public information, so
    // it is the side of the comparison that may be held as a string.
    Ok(from_key.output_matches(&from_certificate.stdout))
}

#[cfg(test)]
#[path = "../tests/ssl/key_matches_certificate_tests.rs"]
mod tests;
