//! The TLS server block for a site with a certificate installed.

use askama::Template;

use crate::render_error::RenderError;

/// Renders the second, port-443 `server` block for a site whose certificate
/// is installed.
///
/// Holds only what a TLS block adds over a plain one: the certificate paths
/// and the protocol policy. The `server_name`/`root`/location directives are
/// specific to the site shape (PHP, static, proxy) and are supplied by the
/// caller as [`Self::server_body`] rather than known here, so this type does
/// not need to grow a case for every site shape that can carry TLS.
///
/// [`Self::server_body`] is NOT assembled by hand: it is the string
/// [`super::site_body::SiteBody`] rendered, and the caller places that same
/// string in the site's port-80 block too. That is the whole point of the
/// seam — a rule added to a site's locations reaches both blocks or neither,
/// where two hand-kept copies would drift and only the TLS half would be
/// wrong, which is the half a browser actually reaches.
#[derive(Template)]
// A config file is not a document: `escape = "none"` because HTML-escaping
// nginx directives corrupts them silently — an apostrophe in a comment came
// out as `&#x27;`, and a body embedded in two blocks was escaped twice. Values
// reaching a template are VALIDATED, never escaped (rules/security.md §4,
// rules/rust.md "Validation first"): `Domain`, `Upstream` and
// `resolve_in_home` are what make them safe to write, and an escaper here
// would only hide a value that had not been through them.
#[template(path = "nginx/ssl_block.conf.j2", escape = "none")]
pub struct SslBlock<'a> {
    /// The primary domain, repeated here for the 443 block's `server_name`.
    pub domain: &'a str,
    /// Additional hostnames, repeated here for the 443 block's `server_name`.
    pub aliases: &'a [String],
    /// Absolute path of the full certificate chain.
    pub certificate_path: &'a str,
    /// Absolute path of the private key.
    pub certificate_key_path: &'a str,
    /// The site-specific `root`, `index` and `location` rules to place inside
    /// the 443 server block — the rendering of
    /// [`super::site_body::SiteBody`], never a string built at the call site.
    pub server_body: &'a str,
}

impl SslBlock<'_> {
    /// Renders the complete `server { listen 443 ssl; ... }` block.
    ///
    /// A site template embeds it inline with the `?` render operator:
    /// `{{ ssl.as_ref().unwrap().render_into_server()? }}`, so a rendering
    /// failure surfaces as the outer site's own [`RenderError`] rather than a
    /// panic.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which
    /// can only happen if the template and this type have drifted apart.
    pub fn render_into_server(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
