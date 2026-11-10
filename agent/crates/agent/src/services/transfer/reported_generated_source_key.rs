//! A finished key generation onto its response.

use maran_ops::transfer::GeneratedSourceKey;

use crate::proto::{GenerateSourceKeyResponse, generate_source_key_response};
use crate::services::transfer::source_failure::SourceFailure;
use crate::services::transfer::to_wire_generated_source_key::to_wire_generated_source_key;

/// Builds the response a key generation ends with. The response has no connection
/// reason: nothing was connected to.
#[must_use]
pub fn reported_generated_source_key(
    outcome: Result<GeneratedSourceKey, SourceFailure>,
) -> GenerateSourceKeyResponse {
    GenerateSourceKeyResponse {
        result: Some(match outcome {
            Ok(key) => generate_source_key_response::Result::Ok(to_wire_generated_source_key(&key)),
            Err(failure) => generate_source_key_response::Result::Error(failure.error),
        }),
    }
}
