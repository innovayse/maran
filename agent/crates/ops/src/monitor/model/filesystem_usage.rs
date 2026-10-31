//! How full a filesystem is.

/// One filesystem's capacity split into the three parts that together make it
/// up: what is occupied, what only root may still write, and what everybody
/// else may still write.
///
/// The three are reported side by side because each answers a question the
/// other two cannot. `used_bytes` is `df`'s `Used`, so the panel never looks
/// wrong next to the tool an operator checks it against. `available_bytes` is
/// `df`'s `Avail`: the room a hosting account — never root — still has, and the
/// figure at whose zero every customer write fails with `ENOSPC`.
/// `reserved_bytes` is the gap between the two that `df` leaves unprinted: the
/// blocks the filesystem keeps for root (5% by default on ext4, none on xfs).
///
/// They partition the filesystem exactly: [`FilesystemUsage::total_bytes`] is
/// their sum and nothing else, so no reading can claim more parts than the
/// whole. The constructor that builds one from `statvfs` lives with the
/// syscall (`process_monitor_host::usage_of`) and clamps a filesystem that
/// reports inconsistent counts so the partition still holds.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct FilesystemUsage {
    /// Bytes the filesystem accounts as occupied: `(f_blocks - f_bfree)`
    /// fragments — `df`'s `Used`.
    pub used_bytes: u64,
    /// Bytes free but writable by root only: `(f_bfree - f_bavail)` fragments.
    /// Zero on a filesystem that keeps no reserve.
    pub reserved_bytes: u64,
    /// Bytes an unprivileged writer may still add: `f_bavail` fragments —
    /// `df`'s `Avail`.
    pub available_bytes: u64,
}

impl FilesystemUsage {
    /// The filesystem's whole capacity: used, reserved and available together,
    /// which is `df`'s `Size`.
    ///
    /// Saturating, so three parts read from a filesystem that reports absurd
    /// counts cannot wrap into a small total.
    #[must_use]
    pub fn total_bytes(&self) -> u64 {
        self.used_bytes
            .saturating_add(self.reserved_bytes)
            .saturating_add(self.available_bytes)
    }
}
