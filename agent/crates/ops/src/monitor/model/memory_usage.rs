//! How much of the host's memory is in use.

/// The field naming how much memory the machine has.
const TOTAL_FIELD: &str = "MemTotal:";

/// The field naming how much can still be handed out without swapping.
const AVAILABLE_FIELD: &str = "MemAvailable:";

/// The unit the kernel writes these fields in: kibibytes, spelled `kB`.
const KIB_IN_BYTES: u64 = 1024;

/// The host's memory, as a total and the part of it that is spoken for.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct MemoryUsage {
    /// Memory in use, in bytes.
    pub used_bytes: u64,
    /// Memory installed, in bytes.
    pub total_bytes: u64,
}

impl MemoryUsage {
    /// Reads the kernel's memory accounting.
    ///
    /// Used is `MemTotal - MemAvailable`, and NOT `MemTotal - MemFree`. The
    /// difference is the whole reason this function exists rather than being
    /// two subtractions at the call site: Linux spends every byte it is not
    /// otherwise using on page cache, so `MemFree` on a healthy, long-running
    /// server is a small number and a panel built on it reports every host as
    /// nearly out of memory. `MemAvailable` is the kernel's own estimate of
    /// what a new process could get, reclaimable cache included, which is the
    /// question a hosting operator is actually asking.
    ///
    /// The fields are read by NAME. Their order in the file is not part of any
    /// interface, and neither is which fields sit between them.
    ///
    /// Values are `kB` in the file's spelling and kibibytes in fact — the
    /// kernel has written 1024-byte units under that label since long before
    /// this panel, and `free`, `top` and every other reader treat it so.
    ///
    /// Returns `None` when either field is absent or is not a number, rather
    /// than reporting a total of zero: a host with no memory is not a fact this
    /// agent should ever assert.
    #[must_use]
    pub fn parse(meminfo: &str) -> Option<Self> {
        let total = field(meminfo, TOTAL_FIELD)?;
        let available = field(meminfo, AVAILABLE_FIELD)?;

        Some(Self {
            used_bytes: total.saturating_sub(available).saturating_mul(KIB_IN_BYTES),
            total_bytes: total.saturating_mul(KIB_IN_BYTES),
        })
    }
}

/// The kibibyte value of the line beginning with `name`.
///
/// The line's shape is `<name><spaces><number> kB`, so the number is the second
/// whitespace-separated token and the unit is the third. The unit is not
/// checked: every field of this file the agent reads is written in `kB`, and a
/// kernel that changed that would be changing an interface `free` and `top`
/// depend on too.
fn field(meminfo: &str, name: &str) -> Option<u64> {
    meminfo
        .lines()
        .find(|line| line.split_whitespace().next() == Some(name))?
        .split_whitespace()
        .nth(1)?
        .parse()
        .ok()
}

#[cfg(test)]
#[path = "../../tests/monitor/memory_usage_tests.rs"]
mod tests;
