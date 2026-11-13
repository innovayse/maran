//! The Rspamd rule that binds locally submitted mail to its account.

use askama::Template;

use crate::render_error::RenderError;

/// The Lua rule itself, shipped byte for byte: it has no per-host value, every
/// path and number it needs comes from [`RspamdLocalSubmit`].
const LUA_RULE: &str = include_str!("../../templates/rspamd/lua/maran_local_submit.lua");

/// The name under which the rule records, on the message it is scanning, the
/// proof that the message's `From:` domain is bound to the submitting account. The
/// one spelling: it is rendered into the settings, and both the rule (which writes
/// the proof) and the DKIM signing condition (which reads it) take it from there.
const BOUND_DOMAIN_KEY: &str = "maran_bound_domain";

/// Renders `local.d/maran_local_submit.conf`: where the rule finds its five maps.
#[derive(Template)]
#[template(path = "rspamd/local_submit.conf.j2", escape = "none")]
pub struct RspamdLocalSubmit {
    /// Absolute path of the directory holding the five account maps.
    pub maps_directory: String,
    /// The most recipients one locally submitted message may name.
    pub max_recipients: u32,
    /// System users whose submissions the rule leaves alone: root and the Postfix
    /// owner, nobody else.
    pub exempt_uids: Vec<u32>,
    /// The host's own mail name (the validated mail server hostname): the only `From:` domain whose
    /// mail from an exempt user is proven for DKIM signing.
    pub host_mail_name: String,
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

    /// The name of the per-message proof of a bound `From:` domain, rendered into
    /// the settings so the rule and the DKIM condition cannot spell it differently.
    #[must_use]
    pub const fn bound_domain_key(&self) -> &'static str {
        BOUND_DOMAIN_KEY
    }

    /// The text of `plugins.d/maran_local_submit.lua`.
    #[must_use]
    pub const fn lua_rule() -> &'static str {
        LUA_RULE
    }
}
