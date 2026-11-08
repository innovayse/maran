//! The whole `named.conf` the agent writes for the authoritative DNS daemon.

use askama::Template;

use crate::render_error::RenderError;

/// The authoritative-only daemon configuration: no recursion, no cache,
/// response rate limiting, a loopback-only control channel, and a zone list the
/// daemon keeps itself (`allow-new-zones`).
///
/// Every field is a platform or agent decision (a path, a key name, a port); NO
/// field comes from a request, so nothing a caller types reaches this text.
/// The security-relevant statements — the recursion refusal and the rate limit
/// — are literals in the template and not fields, on purpose: a field is a
/// thing a caller of the render could turn off, and the golden diff is the
/// review artefact for these lines.
#[derive(Template)]
// A config file is not a document: HTML-escaping a path would corrupt it
// silently. Every value reaching this template is a constant of the agent or
// the distro adapter (rules/rust.md "Validation first"), which is what makes an
// escaper needless here rather than merely inconvenient.
#[template(path = "bind/named.conf.j2", escape = "none")]
pub struct NamedConf {
    /// Path of the key file the control channel and `rndc` both read, included
    /// by the configuration.
    pub control_key_path: String,
    /// Name of the key inside that file; the `controls` statement names it.
    pub control_key_name: String,
    /// Loopback TCP port of the control channel.
    pub control_port: u16,
    /// The daemon's working directory and the directory of the zone list it
    /// keeps itself. A distro fact, so a field and never a literal.
    pub state_directory: String,
}

impl NamedConf {
    /// Renders the configuration.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template and this type have
    /// drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
