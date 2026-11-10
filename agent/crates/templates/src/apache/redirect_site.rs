//! The virtual hosts that send a non-canonical host name to the canonical one.

use askama::Template;

use crate::apache::ssl_block::SslBlock;
use crate::render_error::RenderError;

/// Renders the virtual hosts of the names that redirect to a site's canonical host.
#[derive(Template)]
// A config file is not a document: `escape = "none"` because HTML-escaping
// Apache directives corrupts them silently. Values reaching a template are
// VALIDATED, never escaped (rules/security.md §4, rules/rust.md "Validation
// first"): `Domain`, `Upstream`, `NodeStartCommand` and `resolve_in_home` are what
// make them safe to write, and an escaper here would only hide a value that had
// not been through them.
#[template(path = "apache/redirect_site.conf.j2", escape = "none")]
pub struct RedirectSite<'a> {
    /// The redirected names; the first becomes `ServerName`, the rest `ServerAlias`.
    pub names: &'a [String],
    /// Absolute document root under the account's home, for the challenge alias only.
    pub document_root: &'a str,
    /// What the names answer, already rendered by
    /// [`super::canonical_redirect_body::CanonicalRedirectBody`].
    pub body: &'a str,
    /// The TLS half, when a certificate is installed.
    pub ssl: Option<SslBlock<'a>>,
}

impl RedirectSite<'_> {
    /// Renders the configuration text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
