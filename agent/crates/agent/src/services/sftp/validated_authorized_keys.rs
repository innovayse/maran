//! Turning a `SetSftpAuthorizedKeys` request into the values the operation
//! takes.

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_agent_core::validation::system::ssh_public_key::SshPublicKey;
use maran_ops::sftp::AuthorizedKeySet;

use crate::proto::AgentError;
use crate::services::sftp::validated_sftp_user::validated_sftp_user;
use crate::services::wire::invalid_input::invalid_input;

/// Re-validates the account, the login and every key of a
/// `SetSftpAuthorizedKeys` request (issue #135).
///
/// The key count is refused BEFORE any key is parsed, so a request carrying a
/// million entries costs one comparison rather than a million decodes. One
/// refused key refuses the whole request: a set written with a key silently
/// missing is a deploy pipeline that stops logging in with no reason given. The
/// refusal names the key's position and the rule, never the key text.
///
/// # Errors
///
/// An `INVALID_INPUT` [`AgentError`] when the account or login name is refused,
/// when more than [`AuthorizedKeySet::MAX_KEYS`] keys were supplied, or when a
/// key is not an accepted SSH public key.
pub fn validated_authorized_keys(
    account_username: &str,
    sftp_username: &str,
    public_keys: &[String],
) -> Result<(AccountName, SftpUserName, AuthorizedKeySet), AgentError> {
    let (account, user) = validated_sftp_user(account_username, sftp_username)?;

    if public_keys.len() > AuthorizedKeySet::MAX_KEYS {
        return Err(invalid_input(format!(
            "{} public keys were supplied, over the {}-key ceiling",
            public_keys.len(),
            AuthorizedKeySet::MAX_KEYS
        )));
    }

    let mut keys = Vec::with_capacity(public_keys.len());
    for (position, text) in public_keys.iter().enumerate() {
        let key = SshPublicKey::parse(text)
            .map_err(|error| invalid_input(format!("public key {position}: {error}")))?;
        keys.push(key);
    }

    let set = AuthorizedKeySet::new(keys).map_err(|error| invalid_input(error.to_string()))?;

    Ok((account, user, set))
}

#[cfg(test)]
#[path = "../../tests/services/sftp/validated_authorized_keys_tests.rs"]
mod tests;
