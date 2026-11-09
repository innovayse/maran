//! What the child of an archive operation tells the parent, as bytes.

use super::archive_refusal::ArchiveRefusal;
use crate::files::model::archive_report::ArchiveReport;

/// First byte of an answer that carries a report.
const TAG_DONE: u8 = 0;

/// First byte of an answer that carries a refusal.
const TAG_REFUSED: u8 = 1;

/// Most bytes an answer takes: a tag and two 64-bit figures.
pub(crate) const MAXIMUM_ANSWER_BYTES: usize = 17;

/// The one thing the dropped child says before it exits.
///
/// A fixed, tiny, strictly decoded record, because everything the child writes
/// is hostile input to the root parent: a tag, then either two little-endian
/// 64-bit figures or one refusal code. Anything of another length, tag or code
/// is "the child did not produce an answer" and is never repaired.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum ArchiveAnswer {
    /// The work finished; these are its figures.
    Done(ArchiveReport),
    /// The work was refused, and nothing it created is left.
    Refused(ArchiveRefusal),
}

impl ArchiveAnswer {
    /// The bytes the child writes.
    pub(crate) fn encode(self) -> Vec<u8> {
        match self {
            Self::Done(report) => {
                let mut bytes = Vec::with_capacity(MAXIMUM_ANSWER_BYTES);
                bytes.push(TAG_DONE);
                bytes.extend_from_slice(&report.entries.to_le_bytes());
                bytes.extend_from_slice(&report.bytes.to_le_bytes());
                bytes
            }
            Self::Refused(refusal) => vec![TAG_REFUSED, refusal.code()],
        }
    }

    /// Reads back what [`encode`](Self::encode) wrote; `None` for anything else.
    pub(crate) fn decode(bytes: &[u8]) -> Option<Self> {
        match bytes {
            [TAG_DONE, rest @ ..] if rest.len() == 16 => {
                let entries = u64::from_le_bytes(rest[..8].try_into().ok()?);
                let written = u64::from_le_bytes(rest[8..].try_into().ok()?);
                Some(Self::Done(ArchiveReport {
                    entries,
                    bytes: written,
                }))
            }
            [TAG_REFUSED, code] => ArchiveRefusal::from_code(*code).map(Self::Refused),
            _ => None,
        }
    }
}

#[cfg(test)]
#[path = "../../tests/files/archive/archive_answer_tests.rs"]
mod tests;
