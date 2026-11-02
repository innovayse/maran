//! Turning the term of a `BanAddress` request into the life of the ban.

use std::time::Duration;

use crate::proto::AgentError;
use crate::proto::ban_address_request::Term;
use crate::services::wire::invalid_input::invalid_input;

/// Reads how long a ban lasts out of the request's `term`, falling back to the
/// deprecated `duration_seconds` only when `term` is unset.
///
/// `None` is "no timeout": the ban stays until it is lifted. It is `None` and
/// not a zero `Duration` because `nft` reads `timeout 0s` as no timeout anyway
/// and writing the clause would say the same thing in a form that reads like a
/// mistake.
///
/// - `term` set to `permanent` is `None`, stated explicitly.
/// - `term` set to `expires_after_seconds` is that many seconds. The new field
///   has no "0 means permanent": a timed ban of zero seconds is refused, so a
///   caller that computed a zero by mistake cannot place a permanent ban.
/// - `term` unset is a request from a panel older than revision 13, which
///   states permanence as `duration_seconds == 0`, the one place in this
///   service where a zero is a value rather than an absent field. That reading
///   is kept until the deprecated field is removed.
///
/// # Errors
///
/// Returns the invalid-input wire error when `term` is a timed ban of zero
/// seconds.
pub fn ban_lifetime(
    term: Option<&Term>,
    legacy_duration_seconds: u32,
) -> Result<Option<Duration>, AgentError> {
    match term {
        Some(Term::Permanent(_)) => Ok(None),
        Some(Term::ExpiresAfterSeconds(0)) => Err(invalid_input(
            "a timed ban must last at least one second; send `permanent` for a ban that never expires",
        )),
        Some(Term::ExpiresAfterSeconds(seconds)) => {
            Ok(Some(Duration::from_secs(u64::from(*seconds))))
        }
        None => Ok((legacy_duration_seconds > 0)
            .then(|| Duration::from_secs(u64::from(legacy_duration_seconds)))),
    }
}

#[cfg(test)]
#[path = "../../tests/services/firewall/ban_lifetime_tests.rs"]
mod tests;
