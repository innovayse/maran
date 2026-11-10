//! What a site serves and records, rendered once and used by both of its virtual hosts.

use askama::Template;

use crate::apache::site_options_block::SiteOptionsBlock;
use crate::render_error::RenderError;

/// Renders the directives a site's virtual hosts share: logs, document root, link rule,
/// handler or proxy rules, and the per-site options.
///
/// It is rendered ONCE by the caller and the same string is embedded in the port-80 virtual
/// host and in the TLS one, so a rule added here reaches both halves or neither. Which arm
/// is written is decided by the two optional inputs: `upstream` selects the proxy arm (a
/// reverse-proxied site, or a Node.js site proxied to its loopback port), `fpm_socket` the
/// PHP arm, and neither the static one.
///
/// A symbolic link is followed only when the link and its target share an owner:
/// `Options -FollowSymLinks +SymLinksIfOwnerMatch` is written on the document root of both
/// file-serving arms, with `AllowOverride None` so a customer's `.htaccess` cannot lift it.
/// Without the rule a link in one site's root served another account's files, or the host's.
/// The proxy arm opens no file and writes none.
///
/// Every value is already validated by the caller; the template escapes nothing.
#[derive(Template)]
// A config file is not a document: `escape = "none"` because HTML-escaping
// Apache directives corrupts them silently. Values reaching a template are
// VALIDATED, never escaped (rules/security.md §4, rules/rust.md "Validation
// first"): `Domain`, `Upstream`, `NodeStartCommand` and `resolve_in_home` are what
// make them safe to write, and an escaper here would only hide a value that had
// not been through them.
#[template(path = "apache/site_body.conf.j2", escape = "none")]
pub struct SiteBody<'a> {
    /// Absolute path of the access log, root-owned and outside every home.
    pub access_log: &'a str,
    /// Absolute path of the error log, root-owned and outside every home.
    pub error_log: &'a str,
    /// Absolute document root under the account's home.
    pub document_root: &'a str,
    /// The php-fpm pool socket, which selects the PHP arm.
    pub fpm_socket: Option<&'a str>,
    /// The `host:port` the site is proxied to, which selects the proxy arm.
    pub upstream: Option<&'a str>,
    /// The site's per-site options.
    pub options: &'a SiteOptionsBlock<'a>,
}

impl SiteBody<'_> {
    /// Renders the body, trimmed of trailing whitespace so the caller controls what follows it.
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
