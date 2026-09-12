//! What locking or unlocking one account's logins turned, and what it culled.

use crate::logins::model::account_login_set::AccountLoginSet;

/// The answer to "what did this suspension, or this resume, actually do".
///
/// Two facts that a caller must be able to read apart, which is why they are one
/// value and not one: the account's logins as they stand AFTER the operation, and
/// how many of the account's running processes the operation signalled.
///
/// # Why the cull count is not a field of [`AccountLoginSet`]
///
/// Because that type answers a different question and is returned by things that
/// cull nothing. `account_logins` returns it as a pure enumeration, and
/// `AccountSuspensionState` carries it as an observation of the host made long
/// after any suspension ran. A cull figure on that type would be a number those
/// two callers would have to invent, and an invented zero is precisely the defect
/// this whole value exists to keep off the operator's screen.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AccountLockOutcome {
    /// The account's logins as the host holds them after the operation.
    pub logins: AccountLoginSet,

    /// How many of the account's processes were signalled, or [`None`] when no
    /// cull was attempted.
    ///
    /// [`None`] means the UNLOCK direction and nothing else: a resume ends no
    /// session, so there is no number, and reporting one as zero would tell the
    /// panel a cull ran and found nothing. `Some(0)` is the opposite fact — the
    /// cull ran and `pkill` matched no process, which is a measurement and the
    /// state a suspension is trying to reach.
    ///
    /// There is no third value for a FAILED cull. `end_account_sessions` answers
    /// [`crate::logins::LoginsError::SessionCullFailed`] there and this whole
    /// outcome is never built, because some of the account's processes may have
    /// been signalled and some may not: a suspension that cannot be accounted
    /// for is reported as failed and re-issued, never as a count.
    pub sessions_ended: Option<u32>,
}
