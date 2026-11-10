//! The port-80 virtual host that answers the panel's own ACME challenge.

use askama::Template;

use crate::render_error::RenderError;

/// Renders `maran-acme.conf`: the alias of the panel's challenge directory, for a host whose
/// site web server is Apache and therefore owns port 80.
///
/// With nginx as the site web server the same job is the port-80 server block of the panel's
/// own `maran.conf`. Nothing but the challenge directory is served, and a request for any
/// other path is a `404`; it is not a second way into the panel. The access log format omits
/// the query string, as the panel's nginx one does.
#[derive(Template)]
// A config file is not a document: `escape = "none"` because HTML-escaping
// Apache directives corrupts them silently. Values reaching a template are
// VALIDATED, never escaped (rules/security.md §4, rules/rust.md "Validation
// first"): `Domain`, `Upstream`, `NodeStartCommand` and `resolve_in_home` are what
// make them safe to write, and an escaper here would only hide a value that had
// not been through them.
#[template(path = "apache/panel_acme_site.conf.j2", escape = "none")]
pub struct PanelAcmeSite<'a> {
    /// The panel's host name, which is reserved from every tenant.
    pub server_name: &'a str,
    /// Absolute path of the root-owned directory the challenge files are written to.
    pub challenge_directory: &'a str,
    /// Absolute path of the access log.
    pub access_log: &'a str,
    /// Absolute path of the error log.
    pub error_log: &'a str,
}

impl PanelAcmeSite<'_> {
    /// Renders the configuration text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
