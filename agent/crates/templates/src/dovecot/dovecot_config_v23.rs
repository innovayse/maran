//! The whole Dovecot 2.3 configuration the agent writes.

use askama::Template;

use crate::render_error::RenderError;

/// Renders the complete Dovecot 2.3 configuration, written whole on every apply.
///
/// The 2.3 and 2.4 generations share no syntax for the settings below (the
/// certificate, the passwd-file, the mail location and the variable spelling all
/// differ), so each has its own render type and template; which one a host gets
/// is a fact of its release (`MailFacts::dovecot_major`) and is chosen by the
/// caller, never here.
///
/// UNVERIFIED against a real daemon: this template was written from the vendor's
/// documentation, and the first thing that reads it is `doveconf -n` on the host,
/// which the config-write protocol runs after the swap and rolls back on.
#[derive(Template)]
#[template(path = "dovecot/dovecot_v23.conf.j2", escape = "none")]
pub struct DovecotConfigV23 {
    /// Whether POP3 is served beside IMAP.
    pub pop3_enabled: bool,
    /// Absolute path of the certificate chain.
    pub certificate_path: String,
    /// Absolute path of that certificate's private key.
    pub private_key_path: String,
    /// Absolute path of the passwd-file holding every mailbox.
    pub users_path: String,
    /// Absolute path of the auth socket Postfix's SASL asks.
    pub sasl_socket: String,
    /// Absolute path of the LMTP socket Postfix delivers to.
    pub lmtp_socket: String,
    /// The system user Postfix runs as, which owns both sockets.
    pub postfix_user: String,
}

impl DovecotConfigV23 {
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
