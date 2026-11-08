//! Reading the fixed-size answer of a forked job, trusting nothing in it.

use super::child_refusal::MAXIMUM_REFUSAL_CODE;
use super::child_report::ChildReport;
use super::decoded_report::DecodedReport;
use super::encode_report::REPORT_BYTES;

/// Decodes exactly [`REPORT_BYTES`] bytes. Integers only are read.
pub(super) fn decode_report(bytes: &[u8; REPORT_BYTES]) -> DecodedReport {
    match bytes[0] {
        0 => {
            let mut first = [0_u8; 8];
            let mut second = [0_u8; 8];
            first.copy_from_slice(&bytes[1..9]);
            second.copy_from_slice(&bytes[9..17]);
            DecodedReport::Succeeded(ChildReport {
                first: u64::from_le_bytes(first),
                second: u64::from_le_bytes(second),
            })
        }
        code if code <= MAXIMUM_REFUSAL_CODE => DecodedReport::Refused(code),
        _ => DecodedReport::Malformed,
    }
}
