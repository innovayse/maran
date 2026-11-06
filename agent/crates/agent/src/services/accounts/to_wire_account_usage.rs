//! The one mapping from an account's measured usage onto the wire payload.

use maran_ops::accounts::AccountUsage;

use crate::proto::{GetAccountUsageOk, QuotaUnenforceableReason};
use crate::services::accounts::to_wire_quota_state::to_wire_quota_state;

/// Converts a usage reading into the `GetAccountUsageOk` the contract carries.
#[must_use]
#[allow(deprecated)]
pub fn to_wire_account_usage(usage: AccountUsage) -> GetAccountUsageOk {
    let (quota_state, quota_bytes, quota_unenforceable_reason) = to_wire_quota_state(usage.quota);
    GetAccountUsageOk {
        used_bytes: usage.used_bytes,
        // Deprecated, and still written: it is the byte mirror older callers
        // read, per accounts.proto's own doc comment on this field.
        // `quota_state` is the field new callers must use.
        quota_bytes,
        quota_state: quota_state as i32,
        quota_unenforceable_reason: quota_unenforceable_reason
            .unwrap_or(QuotaUnenforceableReason::Unspecified)
            as i32,
    }
}
