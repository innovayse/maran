//! The fixed shape of the answer a forked job writes to its parent.

use super::child_refusal::{ChildRefusal, MAXIMUM_REFUSAL_CODE};
use super::child_tally::ChildTally;

/// Bytes in a report: one status byte and two little-endian `u64` figures.
pub(super) const REPORT_BYTES: usize = 17;

/// Encodes a job's outcome as `[status][first][second]`.
///
/// Status `0` is success and carries the tally; a refusal number in `1..=63` is written as
/// stated, with both figures zero. A refusal outside that range (including `0`, which would
/// read as success) is written as `0xFF`, which the parent reads as malformed.
pub(super) fn encode_report(outcome: &Result<ChildTally, ChildRefusal>) -> [u8; REPORT_BYTES] {
    let (status, first, second) = match outcome {
        Ok(tally) => (0_u8, tally.first(), tally.second()),
        Err(refusal) if (1..=MAXIMUM_REFUSAL_CODE).contains(&refusal.code()) => {
            (refusal.code(), 0, 0)
        }
        Err(_) => (u8::MAX, 0, 0),
    };
    let mut bytes = [0_u8; REPORT_BYTES];
    bytes[0] = status;
    bytes[1..9].copy_from_slice(&first.to_le_bytes());
    bytes[9..17].copy_from_slice(&second.to_le_bytes());
    bytes
}
