//! What a non-canonical host name answers: a redirect to the canonical one.

use askama::Template;

use crate::render_error::RenderError;

/// Renders the one `location` a canonical-host redirect server block holds.
///
/// A host-level redirect is a `server` block of its own and not a server-level
/// `if`, because an `if` in `server` context runs before location selection and
/// would redirect certificate challenges too. The block this body goes into
/// keeps the challenge location (see [`super::redirect_site::RedirectSite`]).
///
/// The same text serves the port-80 block and the TLS block of the redirect
/// site, so the two cannot disagree. The canonical host is a validated
/// `Domain` (rules/security.md item 4).
#[derive(Template)]
// A config file is not a document; values are validated, never escaped
// (rules/security.md §4, rules/rust.md "Validation first").
#[template(path = "nginx/canonical_redirect_body.conf.j2", escape = "none")]
pub struct CanonicalRedirectBody<'a> {
    /// The host name visitors are sent to.
    pub canonical: &'a str,
    /// True when the canonical site is served over TLS, so the redirect says
    /// `https` and a visitor is never sent from a secure site to a plain one.
    pub secure: bool,
}

impl CanonicalRedirectBody<'_> {
    /// Renders the location without a trailing newline.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which
    /// can only happen if the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render()
            .map(|text| text.trim_end().to_owned())
            .map_err(RenderError::Askama)
    }
}
