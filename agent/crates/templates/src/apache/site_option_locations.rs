//! The extra directives a site's options add.

use askama::Template;

use crate::apache::redirect_rule::RedirectRule;
use crate::render_error::RenderError;

/// Renders the redirect rules and the static-asset cache rule of a site.
#[derive(Template)]
// A config file is not a document: `escape = "none"` because HTML-escaping
// Apache directives corrupts them silently. Values reaching a template are
// VALIDATED, never escaped (rules/security.md §4, rules/rust.md "Validation
// first"): `Domain`, `Upstream`, `NodeStartCommand` and `resolve_in_home` are what
// make them safe to write, and an escaper here would only hide a value that had
// not been through them.
#[template(path = "apache/site_option_locations.conf.j2", escape = "none")]
pub struct SiteOptionLocations {
    /// The site's redirect rules, in the order the visitor meets them.
    pub redirects: Vec<RedirectRule>,
    /// Days static assets may be cached, when the site has the rule.
    pub cache_days: Option<u32>,
}

impl SiteOptionLocations {
    /// Renders the directives, trimmed of trailing whitespace.
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
