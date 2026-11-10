//! The `.key` file of a DNSSEC signing key: its DNSKEY record.

use askama::Template;

use crate::render_error::RenderError;

/// The public-key file of one signing key: a few comment lines and the DNSKEY
/// record the daemon reads as a zone-file line.
///
/// The crate validates nothing: the caller builds every field from a validated
/// zone name and key, whose alphabets hold no newline, quote or control
/// character (rules/security.md item 4). The public half is not a secret.
#[derive(Template)]
// A key file is not a document: HTML-escaping base64 would corrupt it silently.
#[template(path = "bind/dnssec_public_key.conf.j2", escape = "none")]
pub struct DnssecPublicKey<'a> {
    /// The zone apex, fully qualified with its trailing dot.
    pub zone: &'a str,
    /// The RFC 4034 key tag, for the comment line.
    pub key_tag: u16,
    /// The DNSKEY flags field.
    pub flags: u16,
    /// The DNSKEY protocol field.
    pub protocol: u8,
    /// The DNSSEC algorithm number.
    pub algorithm: u8,
    /// The public key as standard base64.
    pub public_key: &'a str,
}

impl DnssecPublicKey<'_> {
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
