//! Reading a `PullAccountFiles` request into validated input, or refusing it.

use maran_agent_core::validation::fs::remote_path::RemotePath;
use maran_agent_core::validation::system::bandwidth_limit::BandwidthLimit;
use maran_ops::transfer::model::pull_phase::PullPhase;
use maran_ops::transfer::model::pull_request::PullRequest;

use crate::proto::{self, PullAccountFilesRequest};
use crate::services::transfer::source_failure::SourceFailure;
use crate::services::transfer::validated_source_credential::validated_source_credential;
use crate::services::transfer::validated_source_endpoint::validated_source_endpoint;
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Revalidates what a `PullAccountFiles` request carries: the source, the key,
/// the account that receives, the source's home, the phase and the limit.
///
/// The endpoint is read first, so a forbidden address is refused with its own
/// connection reason before anything else is looked at. The phase must be one of
/// the two named; an unspecified or unknown number is refused, never defaulted,
/// because the final phase deletes. A bandwidth limit of `0` is "no limit"; any
/// other figure must be one the type accepts.
///
/// # Errors
///
/// `INVALID_INPUT` for any value that does not parse, with the reason
/// `ADDRESS_REFUSED` for a forbidden address literal.
pub fn validated_pull_request(
    request: PullAccountFilesRequest,
) -> Result<PullRequest, SourceFailure> {
    let endpoint = validated_source_endpoint(request.endpoint)?;
    let credential =
        validated_source_credential(request.credential).map_err(SourceFailure::plain)?;
    let account = validated_account(&request.account_username).map_err(SourceFailure::plain)?;
    let source_home = RemotePath::parse(&request.source_home)
        .map_err(|error| SourceFailure::plain(invalid_input(error.to_string())))?;
    let phase = match proto::PullPhase::try_from(request.phase) {
        Ok(proto::PullPhase::Initial) => PullPhase::Initial,
        Ok(proto::PullPhase::Final) => PullPhase::Final,
        _ => {
            return Err(SourceFailure::plain(invalid_input(
                "the pull phase is not one of the two".to_owned(),
            )));
        }
    };
    let bandwidth = if request.bandwidth_limit_kib == 0 {
        None
    } else {
        Some(
            BandwidthLimit::parse(request.bandwidth_limit_kib)
                .map_err(|error| SourceFailure::plain(invalid_input(error.to_string())))?,
        )
    };

    Ok(PullRequest {
        endpoint,
        credential,
        account,
        source_home,
        phase,
        bandwidth,
        expected_bytes: request.expected_bytes,
    })
}

#[cfg(test)]
#[path = "../../tests/services/transfer/validated_pull_request_tests.rs"]
mod tests;
