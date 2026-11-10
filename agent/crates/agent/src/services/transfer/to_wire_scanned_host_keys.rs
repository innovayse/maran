//! The host keys a scan found onto their wire payload.

use maran_ops::transfer::model::scanned_host_key::ScannedHostKey;

use crate::proto;

/// The wire form of a scan's answer, in the order the operation returned them
/// (the agent's order of preference).
///
/// The algorithm is read from the key itself and not stored beside it, so the two
/// fields of a wire key cannot disagree.
#[must_use]
pub fn to_wire_scanned_host_keys(keys: &[ScannedHostKey]) -> proto::ScannedHostKeys {
    proto::ScannedHostKeys {
        keys: keys
            .iter()
            .map(|scanned| proto::ScannedHostKey {
                algorithm: scanned.key.algorithm().to_owned(),
                key: scanned.key.as_str().to_owned(),
                fingerprint: scanned.fingerprint.as_str().to_owned(),
            })
            .collect(),
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/to_wire_scanned_host_keys_tests.rs"]
mod tests;
