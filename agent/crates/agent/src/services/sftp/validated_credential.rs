//! Revalidating the password an SFTP rpc carries.

use maran_agent_core::validation::secrets::password::Password;

use crate::proto::AgentError;
use crate::services::wire::invalid_input::invalid_input;

/// Revalidates the password `CreateSftpUser` and `SetSftpPassword` carry.
///
/// This is not a strength check and it is not politeness about characters. The
/// password becomes half of a `user:password` line written to `chpasswd`'s
/// standard input, and [`Password`] refuses exactly the two characters that
/// would break out of it: a colon moves the boundary between the line's two
/// fields, and a newline ends the line early and starts a second one — and a
/// second `user:password` line is a password set for a login the caller does
/// not own, `root:` included. Neither character has a `Password` value, so
/// neither can arrive at `chpasswd` to be escaped or missed. The value is
/// validated, not escaped (rules/security.md §4).
///
/// Read that before widening the alphabet in `agent-core`. Accepting more
/// characters and quoting them at the call site would replace a guarantee the
/// type system enforces with an escaping routine nobody has reviewed.
///
/// # Errors
///
/// Returns the wire error for a password outside the allowed alphabet, or one
/// outside the allowed length. The message names the condition and **never the
/// value**: `Password` prints itself as `<password>`, and this function builds
/// its message from the error type rather than from what was sent.
pub fn validated_credential(password: &str) -> Result<Password, AgentError> {
    Password::parse(password).map_err(|error| invalid_input(error.to_string()))
}
