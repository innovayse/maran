//! Every bound on what one traffic summary reads and keeps, in one place.
//!
//! The access log is written by the internet at a rate nobody here chooses,
//! and parsed by a root process. Each figure below is enforced by the code that
//! reads or counts (`read_traffic_file`, `traffic_tally`), not stated in a
//! comment and trusted: a log that is enormous, endless, made of one huge line
//! or of a million distinct URLs costs the agent a constant.

/// The most bytes one call reads across the previous rotation and the live
/// file together. A busy site is caught up over several calls; the cursor
/// moves to where the budget stopped, so progress is guaranteed.
pub(crate) const MAXIMUM_READ_BYTES: u64 = 64 * 1024 * 1024;

/// The longest line the tokenizer accepts. A longer one is counted as unparsed
/// and discarded as it streams past, never buffered whole.
pub(crate) const MAXIMUM_LINE_BYTES: usize = 8 * 1024;

/// The most distinct paths one day keeps a counter for. Further new paths add
/// to the day's "other" figure.
pub(crate) const MAXIMUM_DISTINCT_PATHS: usize = 10_000;

/// The most distinct status codes one day keeps a counter for.
pub(crate) const MAXIMUM_STATUS_CODES: usize = 64;

/// The longest path kept. A longer one is counted under "other".
pub(crate) const MAXIMUM_PATH_BYTES: usize = 512;

/// The most paths one day returns, highest first.
pub(crate) const RETURNED_PATHS: usize = 50;

/// The most distinct UTC days one call tallies. nginx writes the time itself,
/// so a call spans a handful of days; the bound only makes that a fact the code
/// holds rather than one it assumes.
pub(crate) const MAXIMUM_DAYS: usize = 16;

/// How many leading bytes of a file its cursor fingerprint covers at most.
pub(crate) const FINGERPRINT_BYTES: u64 = 64;

/// The size of one read from a file.
pub(crate) const READ_CHUNK_BYTES: usize = 256 * 1024;
