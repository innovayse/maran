//! The server blocks that send a non-canonical host name to the canonical one.

use askama::Template;

use crate::nginx::ssl_block::SslBlock;
use crate::render_error::RenderError;

/// Renders the `server` block (and, with a certificate, the TLS one) for the
/// host names of a site that are NOT its canonical host.
///
/// It keeps the certificate-challenge location, because the redirected names
/// are on the site's certificate and their challenges are answered here. The
/// rest of the block is the redirect body.
///
/// The block is appended to the site's own vhost file by the caller; the two
/// halves of one site live in one file so that removing the site removes both.
///
/// Every field is a value the caller has validated (`Domain` names, the
/// resolved document root); a template escapes nothing (rules/security.md
/// item 4).
#[derive(Template)]
// A config file is not a document; values are validated, never escaped
// (rules/security.md §4, rules/rust.md "Validation first").
#[template(path = "nginx/redirect_site.conf.j2", escape = "none")]
pub struct RedirectSite<'a> {
    /// The non-canonical host names, at least one; written as `server_name`.
    pub names: &'a [String],
    /// Absolute document root, written as the challenge location's `root`
    /// together with its own `disable_symlinks if_not_owner;`.
    pub document_root: &'a str,
    /// The redirect body, rendered by
    /// [`super::canonical_redirect_body::CanonicalRedirectBody`] and placed in
    /// both blocks.
    pub body: &'a str,
    /// The TLS half, when a certificate is installed; its `server_body` is
    /// [`Self::body`].
    pub ssl: Option<SslBlock<'a>>,
}

impl RedirectSite<'_> {
    /// Renders the configuration text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which
    /// can only happen if the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
