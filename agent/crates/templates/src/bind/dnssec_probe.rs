//! The probe configuration that asks the daemon's checker whether it can sign.

use askama::Template;

use crate::render_error::RenderError;

/// A configuration holding nothing but the `dnssec-policy` statement that
/// `named.conf` states when the host can sign.
///
/// A BIND build without `dnssec-policy` refuses a whole file on meeting the
/// statement, so the agent asks the daemon's own checker about this file before
/// it renders the statement into the real configuration (issue #452). The probe
/// has no field: nothing a caller types reaches it, and a test pins that its
/// policy line is the same text `named.conf` renders, so the probe cannot accept
/// one statement while the real file states another.
#[derive(Template)]
// A config file is not a document: HTML-escaping would corrupt it silently, and
// there is no value in this template to escape.
#[template(path = "bind/dnssec_probe.conf.j2", escape = "none")]
pub struct DnssecProbe {}

impl DnssecProbe {
    /// Renders the probe.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template and this type have
    /// drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
