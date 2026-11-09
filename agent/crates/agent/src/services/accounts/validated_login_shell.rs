//! Revalidating the login shell a request carries.

use maran_ops::accounts::LoginShell;

use crate::proto::{AgentError, LoginShell as WireLoginShell};
use crate::services::wire::invalid_input::invalid_input;

/// Turns the wire's shell enum into the closed case the operations take.
///
/// Shared by `CreateAccount` and `UnsuspendAccount`, the two requests that give
/// an account a shell, so the reading of "which shell did the panel ask for" is
/// one function and cannot drift between them.
///
/// `UNSPECIFIED` is `NOLOGIN`: it is what a caller written before the field
/// existed sends, and for that caller nologin is exactly what both rpcs always
/// did. A number the enum does not know - a panel newer than this agent that
/// has added a shell - is REFUSED and not read as nologin: guessing a login for
/// a case the agent cannot name is the wrong direction to fail in when the
/// value is a privilege. The contract-revision handshake keeps the two sides
/// close enough that this is the exception, not the way a newer panel is met.
///
/// # Errors
///
/// Returns the wire's invalid-input error naming `login_shell` for a number that
/// is not a case of the enum.
pub fn validated_login_shell(wire: i32) -> Result<LoginShell, AgentError> {
    match WireLoginShell::try_from(wire) {
        Ok(WireLoginShell::Unspecified | WireLoginShell::Nologin) => Ok(LoginShell::Nologin),
        Ok(WireLoginShell::Bash) => Ok(LoginShell::Bash),
        Err(_) => Err(invalid_input(format!(
            "login_shell: {wire} is not a login shell this agent knows"
        ))),
    }
}

#[cfg(test)]
#[path = "../../tests/services/accounts/validated_login_shell_tests.rs"]
mod tests;
