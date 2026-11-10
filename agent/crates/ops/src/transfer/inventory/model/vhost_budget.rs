//! How much configuration text one read may still take in.

use super::inventory_limits::InventoryLimits;

/// The files and bytes of web-server configuration a read may still hold,
/// shared by the nginx and the Apache directories so that the two together stay
/// inside the ceiling.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct VhostBudget {
    /// Files that may still be read.
    files_left: usize,
    /// Bytes that may still be kept.
    bytes_left: usize,
}

impl VhostBudget {
    /// A full budget.
    #[must_use]
    pub fn new() -> Self {
        Self {
            files_left: InventoryLimits::MAX_VHOST_FILES,
            bytes_left: InventoryLimits::MAX_VHOST_BYTES,
        }
    }

    /// Spends one file; `false` when none is left.
    pub fn take_file(&mut self) -> bool {
        match self.files_left.checked_sub(1) {
            Some(left) => {
                self.files_left = left;
                true
            }
            None => false,
        }
    }

    /// Spends `length` bytes; `false` when they do not fit, and nothing is spent.
    pub fn take_bytes(&mut self, length: usize) -> bool {
        match self.bytes_left.checked_sub(length) {
            Some(left) => {
                self.bytes_left = left;
                true
            }
            None => false,
        }
    }
}

impl Default for VhostBudget {
    /// The same as [`VhostBudget::new`].
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
#[path = "../../../tests/transfer/inventory/model/vhost_budget_tests.rs"]
mod tests;
