//! How Rspamd finds the key to sign a domain's mail with.

use askama::Template;

use crate::render_error::RenderError;

/// Renders `local.d/dkim_signing.conf`: the selector map and the key directory.
#[derive(Template)]
#[template(path = "rspamd/dkim_signing.conf.j2", escape = "none")]
pub struct RspamdDkimSigning {
    /// Absolute path of the map from a signing domain to its active selector.
    pub selectors_path: String,
    /// Absolute path of the directory holding `<domain>.<selector>.key` files.
    pub keys_directory: String,
}

impl RspamdDkimSigning {
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
