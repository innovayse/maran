//! The scores at which Rspamd acts on a message.

use askama::Template;

use crate::render_error::RenderError;

/// Renders `local.d/actions.conf`: the greylist, mark-as-spam and reject scores.
///
/// The caller has checked that they rise in that order; this type renders what it
/// is given.
#[derive(Template)]
#[template(path = "rspamd/actions.conf.j2", escape = "none")]
pub struct RspamdActions {
    /// The score at which a message is greylisted.
    pub greylist: u32,
    /// The score at which a message is marked as spam.
    pub add_header: u32,
    /// The score at which a message is rejected.
    pub reject: u32,
}

impl RspamdActions {
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
