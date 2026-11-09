//! The server-level lines that restrict who may read a site.

use askama::Template;

use crate::nginx::ip_rules::IpRules;
use crate::render_error::RenderError;

/// Renders `auth_basic` and the `allow` / `deny` lines of a site.
///
/// Both are server-level directives, so every location of the body inherits
/// them. With both present the default `satisfy all` applies: a client must
/// pass the address rule AND the password.
///
/// An allow-list ends in `deny all;` and a deny-list in `allow all;`, so the
/// default for an address the list does not name is stated in the file and not
/// left to nginx's default.
///
/// Every value is validated by the caller: the credential path is derived by
/// the agent, the networks are `SourceCidr` spellings (rules/security.md
/// item 4).
#[derive(Template)]
// A config file is not a document; values are validated, never escaped
// (rules/security.md §4, rules/rust.md "Validation first").
#[template(path = "nginx/site_access_rules.conf.j2", escape = "none")]
pub struct SiteAccessRules<'a> {
    /// The credential file, when the site has HTTP basic authentication.
    pub auth_user_file: Option<&'a str>,
    /// The IP rules, when the site has any.
    pub ip_rules: Option<IpRules<'a>>,
}

impl SiteAccessRules<'_> {
    /// Renders the lines without a trailing newline.
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
