//! The root-owned key file one SFTP login authenticates with.

use askama::Template;

use crate::render_error::RenderError;

/// Renders an SFTP login's `authorized_keys` file: one
/// `restrict <algorithm> <base64>` line per key.
///
/// `restrict` is written by the TEMPLATE, never by a caller, and it is the one
/// option any line carries: it turns off every forwarding, the agent, X11 and
/// the pty, and it switches off whatever OpenSSH adds later, so a key here is a
/// file-transfer credential and nothing more even if the `Match Group` block
/// were ever loosened.
///
/// Each entry of `keys` is `SshPublicKey::as_line` from `maran-agent-core`: an
/// allow-listed algorithm and a base64 field that decoded to a well-formed key,
/// with no option, no comment and no control character. The file is
/// line-oriented and sshd reads it as root's decision about who may log in, so
/// a newline in a value would be a second key of somebody else's choosing
/// (rules/security.md §4). The values are validated, not escaped.
#[derive(Template)]
// A config file is not a document: HTML-escaping would corrupt the base64
// (`+` and `/` survive, but `=` padding is not the escaper's business either).
// Values reaching a template are validated (rules/rust.md "Validation first").
#[template(path = "sshd/authorized_keys.j2", escape = "none")]
pub struct AuthorizedKeys<'a> {
    /// The login the file belongs to, for the header comment.
    pub login: &'a str,
    /// The keys, each `<algorithm> <base64>`, already de-duplicated.
    pub keys: &'a [String],
}

impl AuthorizedKeys<'_> {
    /// Renders the file text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which
    /// can only happen if the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
