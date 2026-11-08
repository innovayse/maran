//! Counting the whole lines of one log file between an offset and its end.

use crate::sites::SitesOpError;
use crate::sites::model::read_progress::ReadProgress;
use crate::sites::parse_access_log_line::parse_access_log_line;
use crate::sites::site_log_file::SiteLogFile;
use crate::sites::traffic_budget::{MAXIMUM_LINE_BYTES, READ_CHUNK_BYTES};
use crate::sites::traffic_tally::TrafficTally;

/// The line being assembled across reads.
struct LineBuffer {
    /// The bytes of the current line so far, never more than the line cap.
    bytes: Vec<u8>,
    /// `true` once the current line outgrew the cap: the rest of it is dropped
    /// as it streams past, so a single enormous line is never held.
    discarding: bool,
}

impl LineBuffer {
    /// Adds the bytes of a line's next piece.
    fn push(&mut self, piece: &[u8]) {
        if self.discarding {
            return;
        }

        if self.bytes.len() + piece.len() > MAXIMUM_LINE_BYTES {
            self.discarding = true;
            self.bytes.clear();
            return;
        }

        self.bytes.extend_from_slice(piece);
    }

    /// Ends the current line, counting it, and starts the next.
    fn finish(&mut self, tally: &mut TrafficTally) {
        if self.discarding {
            tally.count_unparsed();
        } else if let Some(parsed) = parse_access_log_line(&self.bytes) {
            tally.count(&parsed);
        } else {
            tally.count_unparsed();
        }

        self.bytes.clear();
        self.discarding = false;
    }
}

/// Counts the lines of `file` from `from` to the end the file had when this
/// began, into `tally`, reading at most `budget` bytes.
///
/// **Only whole lines are counted.** The offset returned never passes the last
/// newline, so a line the web server is still writing is read once, complete,
/// on a later call and never twice in halves. Two exceptions, both chosen: with
/// `flush_partial` (a rotated file, which nothing appends to any more) a
/// trailing line without a newline is counted as the line it is; and a line
/// being discarded for outgrowing the cap advances the offset as it goes, so a
/// "line" larger than the whole budget cannot pin the cursor in place forever.
///
/// The length is sampled once, so a file that keeps growing is read to the
/// sample and no further, and `budget` (decremented by every byte read) is what
/// makes the read end for a log of any size. `finished` is `false` only when the
/// budget stopped the read first.
///
/// # Errors
///
/// Returns [`SitesOpError::LogUnreadable`] when the file's length or a read of
/// it fails.
pub(crate) fn read_traffic_file(
    file: &dyn SiteLogFile,
    from: u64,
    budget: &mut u64,
    flush_partial: bool,
    tally: &mut TrafficTally,
) -> Result<ReadProgress, SitesOpError> {
    let end = file.length()?;
    let mut position = from;
    let mut consumed = from;
    let mut line = LineBuffer {
        bytes: Vec::new(),
        discarding: false,
    };
    let mut buffer = vec![0_u8; READ_CHUNK_BYTES];

    while position < end {
        if *budget == 0 {
            return Ok(ReadProgress {
                offset: consumed,
                finished: false,
            });
        }

        let wanted = usize::try_from(end - position)
            .unwrap_or(usize::MAX)
            .min(READ_CHUNK_BYTES)
            .min(usize::try_from(*budget).unwrap_or(usize::MAX));
        let read = file.read_at(position, &mut buffer[..wanted])?;
        if read == 0 {
            // The file ended before the sample said it would: it was
            // truncated underneath this read. What was counted stands.
            break;
        }

        *budget = budget.saturating_sub(read as u64);

        let mut rest = &buffer[..read];
        let mut at = position;
        while let Some(newline) = rest.iter().position(|byte| *byte == b'\n') {
            line.push(&rest[..newline]);
            line.finish(tally);
            at += newline as u64 + 1;
            consumed = at;
            rest = &rest[newline + 1..];
        }
        line.push(rest);

        position += read as u64;
        if line.discarding {
            consumed = position;
        }
    }

    if flush_partial && position >= end && (!line.bytes.is_empty() || line.discarding) {
        line.finish(tally);
        consumed = position;
    }

    Ok(ReadProgress {
        offset: consumed,
        finished: position >= end,
    })
}

#[cfg(test)]
#[path = "../tests/sites/read_traffic_file_tests.rs"]
mod tests;
