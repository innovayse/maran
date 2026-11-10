//! The vhost for a PHP-backed site.

use askama::Template;

use crate::apache::site_options_block::SiteOptionsBlock;
use crate::apache::ssl_block::SslBlock;
use crate::render_error::RenderError;

/// Renders the Apache virtual host for a site served by php-fpm through `proxy_fcgi`.
///
/// Every field is a value that has already been validated by the caller —
/// `agent-core`'s `Domain`, `Upstream` and `resolve_in_home` — because a
/// template escapes nothing (rules/security.md).
#[derive(Template)]
// A config file is not a document: `escape = "none"` because HTML-escaping
// Apache directives corrupts them silently. Values reaching a template are
// VALIDATED, never escaped (rules/security.md §4, rules/rust.md "Validation
// first"): `Domain`, `Upstream`, `NodeStartCommand` and `resolve_in_home` are what
// make them safe to write, and an escaper here would only hide a value that had
// not been through them.
#[template(path = "apache/php_site.conf.j2", escape = "none")]
pub struct PhpSite<'a> {
    /// The primary domain, as `ServerName`.
    pub domain: &'a str,
    /// Additional hostnames served by the same block, as `ServerAlias` lines.
    pub aliases: &'a [String],
    /// Absolute document root under the account's home.
    ///
    /// Written here only as the ACME challenge alias's target, whose directory carries its
    /// own link rule: with a certificate this block holds that alias and a redirect and
    /// none of [`Self::body`], so it inherits no link rule from it.
    pub document_root: &'a str,
    /// What this site serves — its logs, `DocumentRoot`, the handler that hands `.php` files to the pool's socket and locations — already rendered by [`super::site_body::SiteBody`].
    ///
    /// Passed in rather than expanded here so that the SAME string is placed in this block
    /// and in [`SslBlock::server_body`]: a TLS site and its plain-HTTP twin cannot serve
    /// different rules if there is only one rendering of them (rules/security.md — the
    /// failure is silent, and only on the half a browser reaches).
    pub body: &'a str,
    /// The site's options. The challenge alias is declared with its own access rule, so a
    /// site-wide password or address list set by the body does not reach the certificate
    /// authority's request.
    pub options: &'a SiteOptionsBlock<'a>,
    /// The TLS half, when a certificate is installed.
    pub ssl: Option<SslBlock<'a>>,
}

impl PhpSite<'_> {
    /// Renders the configuration text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which can only happen
    /// if the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
