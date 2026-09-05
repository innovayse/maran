//! Turning one live ban into the wire's ban message.

use maran_ops::firewall::ActiveBan;

use crate::proto::BanEntry;

/// The value the two deprecated fields of a listed ban carry.
///
/// The agent stores no reason (`firewall.proto` says why), and it does not
/// produce an absolute expiry: what the kernel holds is a REMAINING timeout,
/// and turning one into an instant needs a clock reading this agent
/// deliberately does not take. A 0 here is "unproduced", never "permanent" —
/// permanent is `expires_in_seconds` being absent.
const UNPRODUCED_EXPIRY: i64 = 0;

/// Builds the wire message for one ban the kernel is holding.
///
/// A lifetime longer than a `uint32` of seconds — about 136 years — is reported
/// as the largest value the field can carry rather than being dropped to
/// absent. Absent means "permanent, reconcile it forever", and a panel that
/// read a 137-year ban as permanent would keep re-applying it after the kernel
/// had let it go. Saturating keeps the answer wrong only in the direction that
/// expires.
#[must_use]
pub fn listed_ban(ban: &ActiveBan) -> BanEntry {
    BanEntry {
        address: ban.address.to_string(),
        reason: String::new(),
        expires_at_unix: UNPRODUCED_EXPIRY,
        expires_in_seconds: ban
            .expires_in
            .map(|lifetime| u32::try_from(lifetime.as_secs()).unwrap_or(u32::MAX)),
    }
}

#[cfg(test)]
#[path = "../../tests/services/firewall/listed_ban_tests.rs"]
mod tests;
