//! Turning a trash-purge request into a validated input, or refusing it.

use std::num::NonZeroU32;

use maran_ops::files::model::purge_expired_trash_input::PurgeExpiredTrashInput;

use crate::proto::{AgentError, PurgeExpiredTrashRequest};
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Rebuilds a validated purge from what the panel sent.
///
/// A retention of zero days is refused: read literally it would mean "older
/// than now", which is the whole trash, and a caller that omitted the field
/// must not empty a customer's trash by accident. The type is a
/// [`NonZeroU32`], so no layer below can be handed a zero.
///
/// # Errors
///
/// Returns the wire error when the account name is invalid or
/// `older_than_days` is zero.
pub fn validated_purge(
    request: &PurgeExpiredTrashRequest,
) -> Result<PurgeExpiredTrashInput, AgentError> {
    let account = validated_account(&request.account_username)?;
    let older_than_days = NonZeroU32::new(request.older_than_days)
        .ok_or_else(|| invalid_input("older_than_days must be at least 1".to_owned()))?;

    Ok(PurgeExpiredTrashInput {
        account,
        older_than_days,
    })
}

#[cfg(test)]
#[path = "../../tests/services/files/validated_purge_tests.rs"]
mod tests;
