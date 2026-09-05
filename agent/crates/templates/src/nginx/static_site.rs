//! The vhost for a site with no application server, served straight from disk.

use askama::Template;

use crate::nginx::ssl_block::SslBlock;
use crate::render_error::RenderError;

/// Renders the nginx server block for a purely static site.
///
/// Every field is a value that has already been validated by the caller —
/// `agent-core`'s `Domain` and `resolve_in_home` — because a template
/// escapes nothing (rules/security.md).
#[derive(Template)]
// A config file is not a document: `escape = "none"` because HTML-escaping
// nginx directives corrupts them silently — an apostrophe in a comment came
// out as `&#x27;`, and a body embedded in two blocks was escaped twice. Values
// reaching a template are VALIDATED, never escaped (rules/security.md §4,
// rules/rust.md "Validation first"): `Domain`, `Upstream` and
// `resolve_in_home` are what make them safe to write, and an escaper here
// would only hide a value that had not been through them.
#[template(path = "nginx/static_site.conf.j2", escape = "none")]
pub struct StaticSite<'a> {
    /// The primary domain, as `server_name`'s first value.
    pub domain: &'a str,
    /// Additional hostnames served by the same block.
    pub aliases: &'a [String],
    /// Absolute document root under the account's home.
    pub document_root: &'a str,
    /// The location rules this site serves, already rendered by
    /// [`super::site_body::SiteBody`].
    ///
    /// Passed in rather than expanded here so that the SAME string is placed
    /// in this block and in [`SslBlock::server_body`]: a TLS site and its
    /// plain-HTTP twin cannot serve different rules if there is only one
    /// rendering of them (rules/security.md — the failure is silent, and only
    /// on the half a browser reaches).
    pub body: &'a str,
    /// The TLS half, when a certificate is installed.
    pub ssl: Option<SslBlock<'a>>,
}

impl StaticSite<'_> {
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
