//! Where Rspamd keeps its rate-limit counters.

use askama::Template;

use crate::render_error::RenderError;

/// Renders `local.d/redis.conf`: the key-value server's unix socket.
#[derive(Template)]
#[template(path = "rspamd/redis.conf.j2", escape = "none")]
pub struct RspamdRedis {
    /// Absolute path of the key-value server's unix socket.
    pub socket_path: String,
}

impl RspamdRedis {
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
