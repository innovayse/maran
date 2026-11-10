//! Reading the endpoint and the credential of an authenticated source rpc.

use maran_ops::transfer::{SourceCredential, SourceEndpoint};

use crate::proto;
use crate::services::transfer::source_failure::SourceFailure;
use crate::services::transfer::validated_source_credential::validated_source_credential;
use crate::services::transfer::validated_source_endpoint::validated_source_endpoint;

/// Revalidates what `TestSource` and `ReadSourceInventory` both carry: where to
/// connect and how to authenticate.
///
/// The endpoint is read first, so a forbidden address is refused with its own
/// connection reason before the credential is looked at.
///
/// # Errors
///
/// The failure of either half: `INVALID_INPUT`, with the reason `ADDRESS_REFUSED`
/// for a forbidden address literal.
pub fn validated_source_inputs(
    endpoint: Option<proto::SourceEndpoint>,
    credential: Option<proto::SourceCredential>,
) -> Result<(SourceEndpoint, SourceCredential), SourceFailure> {
    let endpoint = validated_source_endpoint(endpoint)?;
    let credential = validated_source_credential(credential).map_err(SourceFailure::plain)?;

    Ok((endpoint, credential))
}

#[cfg(test)]
#[path = "../../tests/services/transfer/validated_source_inputs_tests.rs"]
mod tests;
