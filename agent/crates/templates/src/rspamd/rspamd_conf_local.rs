//! The `rspamd.conf.local` section that loads the local-submission rule's settings.

use askama::Template;

use crate::render_error::RenderError;

/// Renders `rspamd.conf.local`: the section that includes
/// `local.d/maran_local_submit.conf`.
///
/// Rspamd auto-includes `local.d/<name>.conf` only for modules it ships, so the
/// rule, which is installed as a module under `plugins.d/`, gets its settings
/// through this section. The file has no per-host value.
#[derive(Template)]
#[template(path = "rspamd/conf_local.conf.j2", escape = "none")]
pub struct RspamdConfLocal;

impl RspamdConfLocal {
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
