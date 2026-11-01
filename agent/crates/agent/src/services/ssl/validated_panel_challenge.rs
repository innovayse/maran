//! The proto → validated input bundle of `WritePanelChallenge`.

use maran_agent_core::validation::web::acme_key_authorization::AcmeKeyAuthorization;
use maran_agent_core::validation::web::acme_token::AcmeToken;

use crate::proto::{AgentError, WritePanelChallengeRequest};
use crate::services::ssl::validated_panel_token::validated_panel_token;
use crate::services::wire::invalid_input::invalid_input;

/// A token and the key authorization answering it, both validated.
#[derive(Debug)]
pub struct ValidatedPanelChallenge {
    /// The challenge token, which names the file.
    pub token: AcmeToken,
    /// The file's content, held to the token it answers.
    pub key_authorization: AcmeKeyAuthorization,
}

impl ValidatedPanelChallenge {
    /// Validates both fields of `request`.
    ///
    /// # Errors
    ///
    /// Returns `ERROR_CODE_INVALID_INPUT` when the token is not base64url of
    /// an accepted length, or the key authorization is not that token, a dot
    /// and a base64url thumbprint.
    pub fn parse(request: &WritePanelChallengeRequest) -> Result<Self, AgentError> {
        let token = validated_panel_token(&request.token)?;
        let key_authorization = AcmeKeyAuthorization::parse(&token, &request.key_authorization)
            .map_err(|error| invalid_input(error.to_string()))?;

        Ok(Self {
            token,
            key_authorization,
        })
    }
}

#[cfg(test)]
#[path = "../../tests/services/ssl/validated_panel_challenge_tests.rs"]
mod tests;
