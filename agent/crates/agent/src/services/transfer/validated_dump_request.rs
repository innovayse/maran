//! Reading a `StreamRemoteDatabase` request into validated input, or refusing it.

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::remote_database_name::RemoteDatabaseName;
use maran_ops::transfer::model::dump_request::DumpRequest;

use crate::proto::StreamRemoteDatabaseRequest;
use crate::services::transfer::source_failure::SourceFailure;
use crate::services::transfer::validated_source_credential::validated_source_credential;
use crate::services::transfer::validated_source_endpoint::validated_source_endpoint;
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// The agent's own ceiling on one database's dump, in bytes: 32 GiB. The smaller
/// of this and the request's figure applies.
const DUMP_CEILING_BYTES: u64 = 32 * 1024 * 1024 * 1024;

/// Revalidates what a `StreamRemoteDatabase` request carries: the source, the
/// key, the database on the source, and the database on this host.
///
/// The target is decoded against the account named in the request, so a database
/// of another account, or one this agent could not have created, is refused here
/// and never reaches the loader. The allowance is the request's figure bounded by
/// the agent's own ceiling; `0` means the ceiling.
///
/// # Errors
///
/// `INVALID_INPUT` for any value that does not parse, with the reason
/// `ADDRESS_REFUSED` for a forbidden address literal.
pub fn validated_dump_request(
    request: StreamRemoteDatabaseRequest,
) -> Result<DumpRequest, SourceFailure> {
    let endpoint = validated_source_endpoint(request.endpoint)?;
    let credential =
        validated_source_credential(request.credential).map_err(SourceFailure::plain)?;
    let source_database = RemoteDatabaseName::parse(&request.source_database)
        .map_err(|error| SourceFailure::plain(invalid_input(error.to_string())))?;
    let account = validated_account(&request.account_username).map_err(SourceFailure::plain)?;
    let target_database =
        DatabaseName::decode(&account, &request.target_database).ok_or_else(|| {
            SourceFailure::plain(invalid_input(
                "the target database is not a database of the account".to_owned(),
            ))
        })?;
    let max_bytes = if request.max_bytes == 0 {
        DUMP_CEILING_BYTES
    } else {
        request.max_bytes.min(DUMP_CEILING_BYTES)
    };

    Ok(DumpRequest {
        endpoint,
        credential,
        source_database,
        target_database,
        max_bytes,
    })
}

#[cfg(test)]
#[path = "../../tests/services/transfer/validated_dump_request_tests.rs"]
mod tests;
