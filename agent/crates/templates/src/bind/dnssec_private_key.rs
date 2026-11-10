//! The `.private` file of a DNSSEC signing key, in the daemon's key format.

use askama::Template;

use crate::render_error::RenderError;

/// The private-key file of one ECDSA P-256 signing key: format version 1.3, the
/// algorithm and the base64 scalar, with timing lines fixed in the past so the
/// daemon adopts the key as already active.
///
/// The crate validates nothing: the caller builds both fields from a validated
/// key (`DnssecKeyMaterial`), whose private half is base64 of 32 bytes and so
/// carries no newline or control character (rules/security.md item 4). The type
/// derives no `Debug`, so a value of it cannot reach a log line by being
/// formatted, and the template does not escape what it writes.
#[derive(Template)]
// A key file is not a document: HTML-escaping base64 would corrupt it silently.
#[template(path = "bind/dnssec_private_key.conf.j2", escape = "none")]
pub struct DnssecPrivateKey<'a> {
    /// The DNSSEC algorithm number, 13 for ECDSAP256SHA256.
    pub algorithm: u8,
    /// The private scalar as standard base64.
    pub private_key: &'a str,
}

impl DnssecPrivateKey<'_> {
    /// Renders the file.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template and this type have
    /// drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
