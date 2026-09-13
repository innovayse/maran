//! The one mapping from a classified shadow field onto the wire enum.

use maran_ops::accounts::StoredPassword;

use crate::proto::LoginPasswordState;

/// Converts the classified shadow password field into the enum the contract
/// carries on `GetAccountSuspensionStateOk`.
///
/// It lives beside the service rather than inside it so that one state maps to
/// one wire value in exactly one place (rules/rust.md "Service anatomy").
///
/// [`LoginPasswordState::Unspecified`] is deliberately not produced here: on
/// the wire it means "the agent predates this field", and an agent that can
/// classify the field always has one of the four real answers. A caller that
/// receives it is talking to an older agent and falls back to `login_locked`.
///
/// Nothing here can leak a hash: [`StoredPassword`] holds no bytes in any
/// variant, so the input to this function is already four inhabitants wide.
#[must_use]
pub fn to_login_password_state(stored: StoredPassword) -> LoginPasswordState {
    match stored {
        StoredPassword::Empty => LoginPasswordState::Empty,
        StoredPassword::Absent => LoginPasswordState::Absent,
        StoredPassword::Locked => LoginPasswordState::Locked,
        StoredPassword::Usable => LoginPasswordState::Usable,
    }
}

#[cfg(test)]
#[path = "../../tests/services/accounts/to_login_password_state_tests.rs"]
mod tests;
