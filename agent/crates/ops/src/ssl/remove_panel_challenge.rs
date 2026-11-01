//! RemovePanelChallenge: taking the panel's challenge file away again.

use maran_agent_core::validation::web::acme_token::AcmeToken;

use crate::ssl::panel_challenge_host::PanelChallengeHost;
use crate::ssl::ssl_op_error::SslOpError;
use crate::ssl::write_panel_challenge::challenge_path;

/// Removes the challenge file for `token` once the authority has read it, or
/// once the order failed (issue #132).
///
/// Not floor-gated: removing a file frees space. Idempotent in the contract's
/// sense — removing a file that is not there reports
/// [`SslOpError::ChallengeNotFound`], which the panel treats as done.
///
/// # Errors
///
/// Returns [`SslOpError::ChallengeNotFound`] when no file is in place for the
/// token, and [`SslOpError::ChallengeWrite`] when it cannot be removed.
pub fn remove_panel_challenge(
    host: &dyn PanelChallengeHost,
    token: &AcmeToken,
) -> Result<(), SslOpError> {
    host.remove_challenge(&challenge_path(token))
}

#[cfg(test)]
#[path = "../tests/ssl/remove_panel_challenge_tests.rs"]
mod tests;
