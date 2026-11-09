//! The whole `master.cf` the agent writes for the host's Postfix.

use askama::Template;

use crate::render_error::RenderError;

/// Renders the complete `master.cf`: the public SMTP service, the two
/// authenticated submission services, and the internal services Postfix needs.
///
/// It takes no value. Every service is unchrooted, because the sockets it talks
/// to are absolute paths outside the queue directory and the two families ship
/// different defaults for the chroot column; that decision is the whole reason
/// the file is Maran's and not the package's.
#[derive(Template)]
#[template(path = "postfix/master.cf.j2", escape = "none")]
pub struct PostfixMasterConfig {}

impl PostfixMasterConfig {
    /// Renders the complete `master.cf` text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template and this type have
    /// drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
