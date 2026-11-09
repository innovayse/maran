//! Rspamd's milter worker, as an `override.d` file.

use askama::Template;

use crate::render_error::RenderError;

/// Renders `override.d/worker-proxy.inc`: the milter Postfix talks to.
///
/// An override and not a `local.d` addition, because an addition is merged with
/// the packaged `bind_socket` and would leave the packaged loopback port bound as
/// well as the socket.
#[derive(Template)]
#[template(path = "rspamd/worker_proxy.inc.j2", escape = "none")]
pub struct RspamdWorkerProxy {
    /// Absolute path of the unix socket to listen on.
    pub socket_path: String,
    /// The group allowed to connect to the socket (Postfix's).
    pub postfix_group: String,
}

impl RspamdWorkerProxy {
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
