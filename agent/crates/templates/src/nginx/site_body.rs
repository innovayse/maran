//! The location rules a site serves, rendered once and used by both of its
//! server blocks.

use askama::Template;

use crate::render_error::RenderError;

/// Renders the part of a site's configuration that says what it actually
/// serves and what it records: its logs, its `root`, its `index` and its
/// `location` blocks.
///
/// This exists because a site with a certificate has TWO server blocks — the
/// port-80 one and [`super::ssl_block::SslBlock`] — and both must serve the
/// same rules. Assembling them separately is how a TLS site quietly ends up
/// answering differently from its plain-HTTP twin: a rule added to one is not
/// a compile error in the other, and nothing on the way to production compares
/// them. Here there is one rendered string, and the caller puts the same bytes
/// in both places, so the two cannot disagree.
///
/// The variant is chosen by which optional field is set, not by a flag the
/// caller could contradict:
///
/// | `upstream` | `fpm_socket` | shape                    |
/// | ---------- | ------------ | ------------------------ |
/// | `Some`     | ignored      | reverse proxy            |
/// | `None`     | `Some`       | php-fpm                  |
/// | `None`     | `None`       | static files             |
///
/// Every field is a value the caller has already validated —
/// `agent-core`'s `Domain`, `Upstream` and `resolve_in_home` — because a
/// template escapes nothing (rules/security.md).
#[derive(Template)]
// A config file is not a document: `escape = "none"` because HTML-escaping
// nginx directives corrupts them silently — an apostrophe in a comment came
// out as `&#x27;`, and a body embedded in two blocks was escaped twice. Values
// reaching a template are VALIDATED, never escaped (rules/security.md §4,
// rules/rust.md "Validation first"): `Domain`, `Upstream` and
// `resolve_in_home` are what make them safe to write, and an escaper here
// would only hide a value that had not been through them.
#[template(path = "nginx/site_body.conf.j2", escape = "none")]
pub struct SiteBody<'a> {
    /// Absolute path of the access log, inside the account's home.
    pub access_log: &'a str,
    /// Absolute path of the error log, inside the account's home.
    pub error_log: &'a str,
    /// Absolute document root under the account's home. Written as `root` for
    /// a file-serving site, and unused by a proxied one, which serves no file
    /// of its own from either block.
    pub document_root: &'a str,
    /// Absolute path of the php-fpm pool's unix socket, for a PHP site.
    pub fpm_socket: Option<&'a str>,
    /// The validated `host:port` a proxied site forwards to.
    pub upstream: Option<&'a str>,
}

impl SiteBody<'_> {
    /// Renders the log directives and the location rules, without a trailing
    /// newline.
    ///
    /// The caller embeds the result in both server blocks verbatim; it is not
    /// re-indented, so the rules carry the four-space indentation a `server`
    /// block expects.
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
