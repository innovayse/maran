//! The TLS virtual host for a site with a certificate installed.

use askama::Template;

use crate::render_error::RenderError;

/// Renders the `*:443` virtual host that sits beside a site's port-80 one.
///
/// The body inside it is the SAME string the port-80 block embeds, so both halves of a
/// site serve identical rules; see [`super::static_site::StaticSite::body`].
#[derive(Template)]
// A config file is not a document: `escape = "none"` because HTML-escaping
// Apache directives corrupts them silently. Values reaching a template are
// VALIDATED, never escaped (rules/security.md §4, rules/rust.md "Validation
// first"): `Domain`, `Upstream`, `NodeStartCommand` and `resolve_in_home` are what
// make them safe to write, and an escaper here would only hide a value that had
// not been through them.
#[template(path = "apache/ssl_block.conf.j2", escape = "none")]
pub struct SslBlock<'a> {
    /// The primary domain, as `ServerName`.
    pub domain: &'a str,
    /// Additional hostnames, as `ServerAlias` lines.
    pub aliases: &'a [String],
    /// Absolute path of the certificate chain the agent installed.
    pub certificate_path: &'a str,
    /// Absolute path of the private key the agent installed.
    pub certificate_key_path: &'a str,
    /// What this block serves, rendered once by the caller.
    pub server_body: &'a str,
}

impl SslBlock<'_> {
    /// Renders the TLS virtual host, to be embedded after the port-80 one.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template and this type have drifted apart.
    pub fn render_into_server(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
