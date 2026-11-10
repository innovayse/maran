//! What a non-canonical host name answers: a redirect to the canonical one.

use askama::Template;

use crate::render_error::RenderError;

/// Renders the one directive that sends every request to the canonical host, path and query
/// string preserved.
#[derive(Template)]
// A config file is not a document: `escape = "none"` because HTML-escaping
// Apache directives corrupts them silently. Values reaching a template are
// VALIDATED, never escaped (rules/security.md §4, rules/rust.md "Validation
// first"): `Domain`, `Upstream`, `NodeStartCommand` and `resolve_in_home` are what
// make them safe to write, and an escaper here would only hide a value that had
// not been through them.
#[template(path = "apache/canonical_redirect_body.conf.j2", escape = "none")]
pub struct CanonicalRedirectBody<'a> {
    /// The canonical host name.
    pub canonical: &'a str,
    /// Whether the redirect goes to `https`; a visitor is never sent from a secure site to a
    /// plain one.
    pub secure: bool,
}

impl CanonicalRedirectBody<'_> {
    /// Renders the directive, trimmed of trailing whitespace.
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
