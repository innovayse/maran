//! The Rspamd rule that binds locally submitted mail to its account.

use askama::Template;

use crate::render_error::RenderError;

/// The Lua rule itself, shipped byte for byte: it has no per-host value, every
/// path and number it needs comes from [`RspamdLocalSubmit`].
const LUA_RULE: &str = include_str!("../../templates/rspamd/lua/maran_local_submit.lua");

/// Renders `local.d/maran_local_submit.conf`: where the rule finds its four maps.
#[derive(Template)]
#[template(path = "rspamd/local_submit.conf.j2", escape = "none")]
pub struct RspamdLocalSubmit {
    /// Absolute path of the directory holding the four account maps.
    pub maps_directory: String,
    /// The most recipients one locally submitted message may name.
    pub max_recipients: u32,
    /// System users whose submissions the rule leaves alone: root and the Postfix
    /// owner, nobody else.
    pub exempt_uids: Vec<u32>,
}

impl RspamdLocalSubmit {
    /// Renders the settings file's text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template and this type have
    /// drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }

    /// The text of `lua.local.d/maran_local_submit.lua`.
    #[must_use]
    pub const fn lua_rule() -> &'static str {
        LUA_RULE
    }
}
