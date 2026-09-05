//! A certificate and the private key that belongs to it.

use std::fmt;

/// The PEM pair an installation is made of: the certificate (leaf, optionally
/// followed by its chain) and its private key.
///
/// One type rather than two `&str` parameters, because the two are only ever
/// meaningful together and because a caller can pass two strings in the wrong
/// order — which here means writing the certificate into the key's file, at the
/// key's mode, and the key into the certificate's, at the certificate's.
///
/// The `Debug` implementation is written by hand and prints no key. A derived
/// one would put the private key into the first `{:?}`, `dbg!`, `tracing`
/// field or `unwrap` panic message anyone ever writes against this type —
/// rules/security.md item 8 names private keys as secrets, and a derive is
/// exactly the kind of leak nobody decides to add.
#[derive(Clone, PartialEq, Eq)]
pub struct CertificateMaterial {
    /// The PEM-encoded certificate, and its chain when there is one.
    certificate_pem: String,
    /// The PEM-encoded private key. Never printed, never logged, never put in
    /// an error.
    private_key_pem: String,
}

impl CertificateMaterial {
    /// The pair as the caller supplied it.
    ///
    /// Nothing is normalised beyond trimming the trailing whitespace a
    /// transport adds: "installing byte-identical material twice is a no-op"
    /// (`ssl.proto`) is decided by comparing what is on disk with what is
    /// offered, and a normalisation applied here but not to the bytes already
    /// installed would make every retry look like a change and rewrite the key.
    #[must_use]
    pub fn new(certificate_pem: &str, private_key_pem: &str) -> Self {
        Self {
            certificate_pem: format!("{}\n", certificate_pem.trim_end()),
            private_key_pem: format!("{}\n", private_key_pem.trim_end()),
        }
    }

    /// The PEM-encoded certificate.
    #[must_use]
    pub fn certificate_pem(&self) -> &str {
        &self.certificate_pem
    }

    /// The PEM-encoded private key.
    ///
    /// The only way to reach the key, so a reviewer greps one name to find
    /// every place it is used: it goes to a spawned tool's stdin and to the
    /// file it is written into, and nowhere else.
    #[must_use]
    pub fn private_key_pem(&self) -> &str {
        &self.private_key_pem
    }
}

impl fmt::Debug for CertificateMaterial {
    /// Prints the certificate's length and nothing about the key.
    ///
    /// Not even the key's length: a length is a small oracle, and there is no
    /// question an operator answers with it that the certificate's own fields
    /// do not answer better.
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter
            .debug_struct("CertificateMaterial")
            .field("certificate_pem_bytes", &self.certificate_pem.len())
            .field("private_key_pem", &"<redacted>")
            .finish()
    }
}
