//! The lines that restrict who may read a site.

use askama::Template;

use crate::nginx::ip_rules::IpRules;
use crate::render_error::RenderError;

/// Renders the authorisation lines of a site that restricts access.
///
/// A password and an address list are BOTH required when both are set, as in the nginx
/// vhost, so they are combined in one `RequireAll`. A deny list carries `Require all granted`
/// beside its `Require not ip`, because a `RequireAll` holding only negations denies every
/// request in Apache.
#[derive(Template)]
// A config file is not a document: `escape = "none"` because HTML-escaping
// Apache directives corrupts them silently. Values reaching a template are
// VALIDATED, never escaped (rules/security.md §4, rules/rust.md "Validation
// first"): `Domain`, `Upstream`, `NodeStartCommand` and `resolve_in_home` are what
// make them safe to write, and an escaper here would only hide a value that had
// not been through them.
#[template(path = "apache/site_access_rules.conf.j2", escape = "none")]
pub struct SiteAccessRules<'a> {
    /// Absolute path of the credential file, when the site has a password.
    pub auth_user_file: Option<&'a str>,
    /// The site's address list, when it has one.
    pub ip_rules: Option<IpRules<'a>>,
}

impl SiteAccessRules<'_> {
    /// Renders the lines, trimmed of trailing whitespace.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render()
            .map(|text| text.trim_end().to_owned())
            .map_err(RenderError::Askama)
    }
}
