//! The vhost left in place for a suspended account.

use askama::Template;

use crate::render_error::RenderError;

/// Renders the nginx server block that replaces a site's own vhost while its
/// account is suspended.
///
/// Carries no upstream or TLS half: a suspended site answers every other
/// request with the same fixed refusal, so nothing else it once served is
/// reachable through it. It still carries [`Self::document_root`], though,
/// because the certificate on a suspended account keeps renewing on schedule
/// (`sites.proto`: the vhost is kept on disable "so SSL renewal and SEO are
/// not disrupted") and Let's Encrypt's HTTP-01 challenge has to find a file
/// under that root or renewal fails silently — leaving the account to come
/// back from suspension with an expired certificate.
///
/// The challenge location carries `disable_symlinks if_not_owner;`, the rule
/// [`super::site_body::SiteBody`] explains. Stubs an earlier agent wrote
/// lack it, and a suspended site is recognised by comparing its file with a
/// render byte for byte (`ops::sites::is_suspended_vhost`), so
/// [`Self::render_legacy_config`] renders that older text for the comparison
/// to accept as well.
#[derive(Template)]
// A config file is not a document: `escape = "none"` because HTML-escaping
// nginx directives corrupts them silently — an apostrophe in a comment came
// out as `&#x27;`, and a body embedded in two blocks was escaped twice. Values
// reaching a template are VALIDATED, never escaped (rules/security.md §4,
// rules/rust.md "Validation first"): `Domain`, `Upstream` and
// `resolve_in_home` are what make them safe to write, and an escaper here
// would only hide a value that had not been through them.
#[template(path = "nginx/suspended_site.conf.j2", escape = "none")]
pub struct SuspendedSite<'a> {
    /// The primary domain, as `server_name`'s first value.
    pub domain: &'a str,
    /// Additional hostnames served by the same block.
    pub aliases: &'a [String],
    /// Absolute webroot used only to answer the ACME HTTP-01 challenge.
    pub document_root: &'a str,
    /// Absolute path of the access log.
    pub access_log: &'a str,
    /// Absolute path of the error log.
    pub error_log: &'a str,
}

/// The rule line the current stub carries and the stubs written before it do
/// not, exactly as it appears in the render.
const SYMLINK_RULE_LINE: &str = "        disable_symlinks if_not_owner;\n";

impl SuspendedSite<'_> {
    /// Renders the configuration text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which
    /// can only happen if the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }

    /// Renders the stub as agents before the symlink rule wrote it.
    ///
    /// Used only to recognise stubs already on disk as suspended; never
    /// written.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] as [`Self::render_config`] does.
    pub fn render_legacy_config(&self) -> Result<String, RenderError> {
        Ok(self.render_config()?.replacen(SYMLINK_RULE_LINE, "", 1))
    }
}

#[cfg(test)]
#[path = "../tests/nginx/suspended_site_tests.rs"]
mod tests;
