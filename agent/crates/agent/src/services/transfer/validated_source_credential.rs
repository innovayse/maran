//! Reading a `SourceCredential` into validated input, or refusing it.

use maran_agent_core::validation::secrets::source_private_key::SourcePrivateKey;
use maran_agent_core::validation::web::host_key::HostKey;
use maran_ops::transfer::model::source_credential::SourceCredential;

use crate::proto::{self, AgentError};
use crate::services::wire::invalid_input::invalid_input;

/// Revalidates the private key and the pinned host key a migration rpc carries.
///
/// The request is taken by value so the key bytes can be overwritten once the
/// validated type holds its own copy: a best effort (the allocator may have copied
/// them on the way in, and the compiler may elide a dead store), but the copy this
/// function controls does not outlive the parse. The key is never part of an
/// error message: the refusal names the rule that failed and nothing of the
/// document.
///
/// # Errors
///
/// `INVALID_INPUT` for an absent credential, a private key that is not an OpenSSH
/// private key document within the bounds, and a host key that is not one
/// `<algorithm> <base64>` pair of an accepted algorithm.
pub fn validated_source_credential(
    credential: Option<proto::SourceCredential>,
) -> Result<SourceCredential, AgentError> {
    let mut credential =
        credential.ok_or_else(|| invalid_input("no source credential".to_owned()))?;

    let parsed = SourcePrivateKey::parse(&credential.private_key);
    credential.private_key.iter_mut().for_each(|byte| *byte = 0);
    let private_key = parsed.map_err(|error| invalid_input(error.to_string()))?;
    let host_key =
        HostKey::parse(&credential.host_key).map_err(|error| invalid_input(error.to_string()))?;

    Ok(SourceCredential {
        private_key,
        host_key,
    })
}

#[cfg(test)]
#[path = "../../tests/services/transfer/validated_source_credential_tests.rs"]
mod tests;
