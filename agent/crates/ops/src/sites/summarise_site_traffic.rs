//! Summarising what a site's access log holds since a cursor.

use std::sync::LazyLock;

use crate::sites::SitesOpError;
use crate::sites::log_file_identity::log_file_identity;
use crate::sites::model::traffic_cursor::TrafficCursor;
use crate::sites::model::traffic_summary::TrafficSummary;
use crate::sites::model::traffic_summary_input::TrafficSummaryInput;
use crate::sites::read_traffic_file::read_traffic_file;
use crate::sites::site_log_file::SiteLogFile;
use crate::sites::site_log_host::SiteLogHost;
use crate::sites::traffic_budget::MAXIMUM_READ_BYTES;
use crate::sites::traffic_run_slots::{HOST_TRAFFIC_SLOTS, TrafficRunSlots};
use crate::sites::traffic_tally::TrafficTally;

/// The host-wide admission count of this process.
static TRAFFIC_SLOTS: LazyLock<TrafficRunSlots> =
    LazyLock::new(|| TrafficRunSlots::new(HOST_TRAFFIC_SLOTS));

/// Where the two files are read from, decided from the cursor.
struct ReadingPlan {
    /// The offset to read the previous rotation from; `None` skips it.
    previous_from: Option<u64>,
    /// The offset to read the live file from.
    live_from: u64,
    /// The cursor named a file that is neither of the two.
    gap: bool,
}

/// Reads a site's access log from `input.cursor` onward and returns the
/// per-UTC-day deltas, the cursor for the next call and whether it caught up.
///
/// Reads the live file and the previous rotation (`.1`, the only archive that
/// is not compressed) and nothing else: no archive is decompressed. A cursor
/// into the live file continues there; a cursor into `.1` means the log rotated
/// since, so `.1` is finished and the live file is read from its start; no
/// cursor starts at `.1` if there is one, then the live file; a cursor naming
/// neither sets `gap` and reads the live file from its start, because `.1` may
/// be partly counted already and guessing which part would double count.
///
/// **Bounded at every end** (`traffic_budget`): at most 64 MiB read per call,
/// 8 KiB per line, 10,000 distinct paths and 64 status codes per day. A host
/// ceiling on concurrent calls is taken FIRST, before any file is opened, and
/// released by drop on every path.
///
/// A cancellation class of **stop**: nothing is written, and the read ends by
/// its own byte budget if the caller goes away.
///
/// # Errors
///
/// Returns [`SitesOpError::TrafficBusy`] when the host's concurrent summaries
/// are all taken, and [`SitesOpError::LogUnreadable`] when the directory is not
/// root's, or a log is a symlink, a FIFO, a device, a hard link or a file root
/// does not own, or cannot be read. A site that has no access log yet is not an
/// error and yields an empty summary with no cursor.
pub fn summarise_site_traffic(
    host: &dyn SiteLogHost,
    input: &TrafficSummaryInput,
) -> Result<TrafficSummary, SitesOpError> {
    summarise_with(&TRAFFIC_SLOTS, host, input, MAXIMUM_READ_BYTES)
}

/// The summary, with its admission count and its byte budget supplied so a
/// test can fill the one and shrink the other.
///
/// # Errors
///
/// As [`summarise_site_traffic`].
fn summarise_with(
    slots: &TrafficRunSlots,
    host: &dyn SiteLogHost,
    input: &TrafficSummaryInput,
    budget_bytes: u64,
) -> Result<TrafficSummary, SitesOpError> {
    let _slot = slots.try_acquire()?;

    let directory = host.open_directory(&input.account)?;
    let live_name = format!("{}.access.log", input.domain.as_str());
    let previous_name = format!("{live_name}.1");
    let live = directory.open_file(&live_name)?;
    let previous = directory.open_file(&previous_name)?;

    let plan = plan_reading(input.cursor.as_ref(), live.as_deref(), previous.as_deref())?;
    let mut budget = budget_bytes;
    let mut tally = TrafficTally::new();
    let mut cursor = None;
    let mut caught_up = true;

    if let (Some(from), Some(file)) = (plan.previous_from, previous.as_deref()) {
        // A rotated file is closed: a trailing line without a newline is a
        // whole line, so it is flushed.
        let progress = read_traffic_file(file, from, &mut budget, true, &mut tally)?;
        cursor = Some(cursor_at(file, progress.offset)?);
        caught_up = progress.finished;
    }

    if caught_up && let Some(file) = live.as_deref() {
        let progress = read_traffic_file(file, plan.live_from, &mut budget, false, &mut tally)?;
        cursor = Some(cursor_at(file, progress.offset)?);
        caught_up = progress.finished;
    }

    let unparsed_lines = tally.unparsed_lines();

    Ok(TrafficSummary {
        days: tally.into_days(),
        cursor,
        caught_up,
        gap: plan.gap,
        unparsed_lines,
    })
}

/// Decides where each file is read from.
///
/// # Errors
///
/// Returns [`SitesOpError::LogUnreadable`] when a file cannot be fingerprinted.
fn plan_reading(
    cursor: Option<&TrafficCursor>,
    live: Option<&dyn SiteLogFile>,
    previous: Option<&dyn SiteLogFile>,
) -> Result<ReadingPlan, SitesOpError> {
    let Some(cursor) = cursor else {
        return Ok(ReadingPlan {
            previous_from: previous.map(|_| 0),
            live_from: 0,
            gap: false,
        });
    };

    if let Some(file) = live
        && names(file, cursor)?
    {
        return Ok(ReadingPlan {
            previous_from: None,
            live_from: cursor.offset,
            gap: false,
        });
    }

    if let Some(file) = previous
        && names(file, cursor)?
    {
        return Ok(ReadingPlan {
            previous_from: Some(cursor.offset),
            live_from: 0,
            gap: false,
        });
    }

    Ok(ReadingPlan {
        previous_from: None,
        live_from: 0,
        gap: true,
    })
}

/// Whether `cursor` names `file`: the file is at least as long as the cursor's
/// offset and carries the identity the cursor recorded.
fn names(file: &dyn SiteLogFile, cursor: &TrafficCursor) -> Result<bool, SitesOpError> {
    Ok(
        file.length()? >= cursor.offset
            && log_file_identity(file, cursor.offset)? == cursor.file_id,
    )
}

/// The cursor that continues `file` from `offset`.
fn cursor_at(file: &dyn SiteLogFile, offset: u64) -> Result<TrafficCursor, SitesOpError> {
    Ok(TrafficCursor {
        file_id: log_file_identity(file, offset)?,
        offset,
    })
}

#[cfg(test)]
#[path = "../tests/sites/summarise_site_traffic_tests.rs"]
mod tests;
