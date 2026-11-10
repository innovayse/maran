//! The vhost left in place for a suspended account.

use askama::Template;

use crate::render_error::RenderError;

/// Renders the Apache virtual host of a suspended site: the ACME challenge stays reachable,
/// everything else answers `403` with a fixed message.
///
/// The rendered file is this area's only record of the suspension, compared byte for byte by
/// `ops::sites::is_suspended_vhost`; the template says why its final line is a comment.
#[derive(Template)]
// A config file is not a document: `escape = "none"` because HTML-escaping
// Apache directives corrupts them silently. Values reaching a template are
// VALIDATED, never escaped (rules/security.md §4, rules/rust.md "Validation
// first"): `Domain`, `Upstream`, `NodeStartCommand` and `resolve_in_home` are what
// make them safe to write, and an escaper here would only hide a value that had
// not been through them.
#[template(path = "apache/suspended_site.conf.j2", escape = "none")]
pub struct SuspendedSite<'a> {
    /// The primary domain, as `ServerName`.
    pub domain: &'a str,
    /// Additional hostnames, as `ServerAlias` lines.
    pub aliases: &'a [String],
    /// Absolute document root, for the challenge alias only.
    pub document_root: &'a str,
    /// Absolute path of the access log.
    pub access_log: &'a str,
    /// Absolute path of the error log.
    pub error_log: &'a str,
}

impl SuspendedSite<'_> {
    /// Renders the configuration text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
