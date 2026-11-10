//! What reading one crontab established.

/// The entries of one crontab and the lines that could not be told apart from
/// noise.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CrontabReading {
    /// Lines that are scheduled entries.
    pub entry_count: u32,
    /// Lines that are neither comments, blanks, environment settings nor
    /// entries, and lines refused for their length or a NUL byte.
    pub skipped_lines: u32,
    /// Whether a bound on lines or bytes cut the reading short.
    pub truncated: bool,
}
