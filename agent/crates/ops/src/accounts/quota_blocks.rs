//! A disk quota in the unit `setquota(8)` and `quota(1)` speak: 1 KiB blocks.

/// Size of one quota block, the unit both tools count in.
const BLOCK_SIZE_BYTES: u64 = 1024;

/// A quota measured in 1 KiB blocks.
///
/// The panel thinks in bytes and the quota tools think in blocks; this type is
/// the one place the two are converted, in both directions, so the rounding
/// rule and the block size cannot drift apart between the operation that sets a
/// quota and the one that reads it back.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(super) struct QuotaBlocks(u64);

impl QuotaBlocks {
    /// Converts a byte count into blocks, rounding UP.
    ///
    /// Rounding down would hand out a quota smaller than the plan the customer
    /// paid for, and the difference would only ever be noticed as an
    /// unexplained write failure.
    #[must_use]
    pub(super) fn from_bytes(quota_bytes: u64) -> Self {
        Self(quota_bytes.div_ceil(BLOCK_SIZE_BYTES))
    }

    /// Reads the hard block limit out of `quota -u -w` output.
    ///
    /// `-w` gives one line per filesystem with no header, which is the only
    /// shape worth parsing; the human table changes between releases. Returns
    /// `None` when there is no quota line, which is the ordinary state of a
    /// filesystem mounted without quotas — not an error, just no limit.
    #[must_use]
    pub(super) fn parse_hard_limit(stdout: &str) -> Option<Self> {
        stdout
            .lines()
            .filter_map(|line| {
                let mut fields = line.split_whitespace();
                let filesystem = fields.next()?;
                if !filesystem.starts_with('/') {
                    return None;
                }

                // Fields after the filesystem: blocks, soft, hard, … The hard limit
                // is the third, and a trailing `*` marks a limit already exceeded.
                let _blocks = fields.next()?;
                let _soft = fields.next()?;
                let hard = fields.next()?;
                hard.trim_end_matches('*').parse::<u64>().ok()
            })
            .next()
            .map(Self)
    }

    /// The quota in bytes, saturating rather than overflowing on an absurd limit.
    #[must_use]
    pub(super) fn to_bytes(self) -> u64 {
        self.0.saturating_mul(BLOCK_SIZE_BYTES)
    }

    /// The block count as `setquota` expects it on the command line.
    #[must_use]
    pub(super) fn as_argument(self) -> String {
        self.0.to_string()
    }
}

#[cfg(test)]
#[path = "../tests/accounts/quota_blocks_tests.rs"]
mod tests;
