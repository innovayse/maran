//! A zone's observed signing onto its wire payload.

use maran_ops::dns::{KeyPublication, SigningMode, ZoneSigningObservation};

use crate::proto::{DnsPolicyObservation, DnsSigningMode, DnsSigningObservation};

/// Builds the `DnsSigningObservation` that `GetZone` answers with.
///
/// Each fact is its own three-valued answer: a mode the daemon could not be asked
/// for is `NOT_OBSERVED` (and the mode field is then the wire's zero, which the
/// contract says is meaningful only when it was observed), and a key the daemon
/// reports nothing for is `NOT_IN_FORCE`, never folded into either of the other
/// two. Nothing the daemon printed is carried: the tag is a number the parser
/// proved and the modes are a closed set.
#[must_use]
pub fn to_wire_zone_signing(observation: &ZoneSigningObservation) -> DnsSigningObservation {
    let (mode, mode_observed) = match observation.mode {
        Some(mode) => (to_wire_mode(mode), DnsPolicyObservation::InForce),
        None => (DnsSigningMode::Unsigned, DnsPolicyObservation::NotObserved),
    };
    let (published_key_tag, published_key_observed) = match observation.key {
        KeyPublication::Published(tag) => (u32::from(tag), DnsPolicyObservation::InForce),
        KeyPublication::NotPublished => (0, DnsPolicyObservation::NotInForce),
        KeyPublication::NotObserved => (0, DnsPolicyObservation::NotObserved),
    };

    DnsSigningObservation {
        mode: mode as i32,
        mode_observed: mode_observed as i32,
        published_key_tag,
        published_key_observed: published_key_observed as i32,
    }
}

/// One mode onto the wire enum.
fn to_wire_mode(mode: SigningMode) -> DnsSigningMode {
    match mode {
        SigningMode::Unsigned => DnsSigningMode::Unsigned,
        SigningMode::Signed => DnsSigningMode::Signed,
        SigningMode::Withdrawing => DnsSigningMode::Withdrawing,
    }
}

#[cfg(test)]
#[path = "../../tests/services/dns/to_wire_zone_signing_tests.rs"]
mod tests;
