//! Revalidating what a `SummariseSiteTraffic` request carries.

use maran_ops::sites::{TrafficCursor, TrafficSummaryInput};

use crate::proto::{AgentError, SummariseSiteTrafficRequest};
use crate::services::sites::validated_existing_identity::validated_existing_identity;
use crate::services::wire::invalid_input::invalid_input;

/// The longest cursor identity accepted. The agent writes `device:inode:hash`,
/// well under fifty characters; the bound only keeps a caller from making the
/// agent compare a megabyte.
const MAXIMUM_FILE_ID_BYTES: usize = 128;

/// Turns a request into the input of the traffic summary.
///
/// The account and the domain are checked for shape only, like the other
/// read-only rpcs on an existing site, so a site created under a name that is
/// reserved today can still be read. The cursor is carried over as it is: it is
/// an opaque identity the agent compares and never interprets as a path, so
/// there is nothing in it to validate beyond its length.
///
/// # Errors
///
/// Returns the wire error for a name or a domain of the wrong shape, and for a
/// cursor identity over the length bound.
pub fn validated_traffic_request(
    request: &SummariseSiteTrafficRequest,
) -> Result<TrafficSummaryInput, AgentError> {
    let (account, domain) =
        validated_existing_identity(&request.account_username, &request.domain)?;

    let cursor = match &request.cursor {
        None => None,
        Some(cursor) if cursor.file_id.len() > MAXIMUM_FILE_ID_BYTES => {
            return Err(invalid_input(format!(
                "the traffic cursor identity is over {MAXIMUM_FILE_ID_BYTES} bytes"
            )));
        }
        Some(cursor) => Some(TrafficCursor {
            file_id: cursor.file_id.clone(),
            offset: cursor.offset,
        }),
    };

    Ok(TrafficSummaryInput {
        account,
        domain,
        cursor,
    })
}

#[cfg(test)]
#[path = "../../tests/services/sites/validated_traffic_request_tests.rs"]
mod tests;
