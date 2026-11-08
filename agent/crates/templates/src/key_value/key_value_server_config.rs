//! The whole configuration file the agent writes for the Redis-protocol server.

use askama::Template;

use crate::render_error::RenderError;

/// Renders the complete key-value server configuration, written whole.
///
/// The packaged file is replaced, not edited: this server exists for the spam
/// filter and serves nothing else, so the TCP listener is switched off and the
/// only way in is the unix socket. The file is valid for Redis and for Valkey,
/// which read the same directives.
#[derive(Template)]
#[template(path = "key_value/server.conf.j2", escape = "none")]
pub struct KeyValueServerConfig {
    /// Absolute path of the unix socket to listen on.
    pub socket_path: String,
}

impl KeyValueServerConfig {
    /// Renders the complete configuration text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template and this type have
    /// drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
