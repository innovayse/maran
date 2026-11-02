//! Turning one live ban into the wire's ban message.

use maran_ops::firewall::ActiveBan;

use crate::proto::BanEntry;

/// Builds the wire message for one ban the kernel is holding.
///
/// The message carries the address and the remaining timeout and nothing else:
/// the agent stores no reason and takes no clock reading to turn a remaining
/// timeout into an instant, so the two fields that once held those (reserved
/// in `firewall.proto`, revision 13) are gone rather than written as zeros.
/// Permanent is `expires_in_seconds` being absent.
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
        expires_in_seconds: ban
            .expires_in
            .map(|lifetime| u32::try_from(lifetime.as_secs()).unwrap_or(u32::MAX)),
    }
}

#[cfg(test)]
#[path = "../../tests/services/firewall/listed_ban_tests.rs"]
mod tests;
