//! The whole `main.cf` the agent writes for the host's Postfix.

use askama::Template;

use crate::render_error::RenderError;

/// Renders the complete `main.cf`, written whole on every apply.
///
/// **Whole, never merged into.** Postfix takes the LAST occurrence of a
/// parameter, so an appended line is not inert: it replaces the rendered one.
/// The file goes through `ops::safe_write`, which replaces it.
///
/// This type renders text and decides nothing. The map type, the socket paths and
/// the certificate paths are platform or agent facts its caller read; every value
/// reaching a field has been validated by the caller, because a newline in a
/// hostname or a path would append parameters to a file a root daemon reads
/// (rules/security.md item 4).
#[derive(Template)]
// A config file is not a document: HTML-escaping a path would corrupt it. Values
// are validated, not escaped (rules/rust.md "Validation first").
#[template(path = "postfix/main.cf.j2", escape = "none")]
pub struct PostfixMainConfig {
    /// The name the server introduces itself with and its certificate is filed under.
    pub hostname: String,
    /// The largest message accepted, in bytes.
    pub message_size_bytes: u64,
    /// The most recipients one message may name.
    pub recipient_limit: u32,
    /// The Postfix lookup-table type every map is written in (`MailFacts::postfix_map_type`).
    pub map_type: String,
    /// Absolute path of the list of served mail domains.
    pub virtual_domains_path: String,
    /// Absolute path of the list of served mailbox addresses.
    pub virtual_mailboxes_path: String,
    /// Absolute path of the alias, forwarder and catch-all table.
    pub virtual_aliases_path: String,
    /// Absolute path of the sender-to-login table.
    pub sender_login_maps_path: String,
    /// Absolute path of the Dovecot LMTP socket mail is delivered to.
    pub lmtp_socket: String,
    /// Absolute path of the Dovecot auth socket SASL asks.
    pub sasl_socket: String,
    /// Absolute path of the Rspamd milter socket.
    pub milter_socket: String,
    /// Absolute path of the certificate chain the server presents.
    pub certificate_path: String,
    /// Absolute path of that certificate's private key.
    pub private_key_path: String,
}

impl PostfixMainConfig {
    /// Renders the complete `main.cf` text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template and this type have
    /// drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
