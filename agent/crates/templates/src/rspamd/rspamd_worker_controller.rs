//! Rspamd's controller worker, as an `override.d` file.

use askama::Template;

use crate::render_error::RenderError;

/// Renders `override.d/worker-controller.inc`: the controller on a root-only unix
/// socket, so the packaged loopback port is not bound.
#[derive(Template)]
#[template(path = "rspamd/worker_controller.inc.j2", escape = "none")]
pub struct RspamdWorkerController {
    /// Absolute path of the unix socket to listen on.
    pub socket_path: String,
}

impl RspamdWorkerController {
    /// Renders the file's text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template and this type have
    /// drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
