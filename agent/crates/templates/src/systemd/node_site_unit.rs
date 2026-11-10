//! The systemd service that runs one Node.js site's application.

use askama::Template;

use crate::render_error::RenderError;

/// Renders the `.service` unit that keeps a Node.js site's application running as its
/// account, on the loopback port its vhost proxies to.
///
/// A unit file is line-oriented and the service manager expands `$` and `%` inside it, and
/// every field is written without escaping (rules/security.md §4). So every field is a value
/// that has already been validated by the caller: `account` an `AccountName`, `domain` a
/// `Domain`, `node_major` a `NodeMajor`, `port` a `Port`, the two paths built by the agent
/// from those, `executable` the distribution adapter's absolute path, and `arguments` the
/// words of a `NodeStartCommand`, whose alphabet excludes whitespace, quotes, `$`, `%`, `;`
/// and every control character. The program is never the caller's text.
#[derive(Template)]
// A unit file is not a document: HTML-escaping its values would corrupt them silently.
// Values reaching a template are validated (rules/rust.md "Validation first"), which is what
// makes an escaper needless here rather than merely inconvenient.
#[template(path = "systemd/node_site_unit.service.j2", escape = "none")]
pub struct NodeSiteUnit<'a> {
    /// The account the application runs as, for `User=`.
    pub account: &'a str,
    /// The site's primary domain, for the description.
    pub domain: &'a str,
    /// The Node.js major the site runs on, for the description.
    pub node_major: &'a str,
    /// Absolute document root, the application's working directory.
    pub document_root: &'a str,
    /// Absolute home of the account, the only path the application may write.
    pub home_directory: &'a str,
    /// The loopback port the application is told to listen on, as `PORT`.
    pub port: u16,
    /// Absolute path of the program, from the distribution adapter.
    pub executable: &'a str,
    /// The validated words after the program.
    pub arguments: &'a [String],
}

impl NodeSiteUnit<'_> {
    /// Renders the unit text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which can only happen
    /// if the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
