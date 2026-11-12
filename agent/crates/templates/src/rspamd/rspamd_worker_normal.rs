//! Rspamd's normal scanner worker, switched off by an `override.d` file.

use askama::Template;

use crate::render_error::RenderError;

/// Renders `override.d/worker-normal.inc`: the worker that listens on a loopback
/// TCP port is disabled.
///
/// Its HTTP protocol takes the road, the login and the client address from request
/// headers any local process can send, so a listening normal worker would let a
/// customer ask for a DKIM signature without going through Postfix. An override and
/// not a `local.d` addition, for the reason the proxy worker's file gives.
#[derive(Template)]
#[template(path = "rspamd/worker_normal.inc.j2", escape = "none")]
pub struct RspamdWorkerNormal;

impl RspamdWorkerNormal {
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
