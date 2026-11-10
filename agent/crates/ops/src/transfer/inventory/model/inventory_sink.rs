//! Where an inventory read puts what it finds.

use super::inventory_step::InventoryStep;
use super::source_account_record::SourceAccountRecord;
use super::source_warning_record::SourceWarningRecord;

/// The receiver of an inventory read, one event at a time, so nothing but the
/// bounded channel behind it holds the inventory.
///
/// Implemented by the service layer over a bounded channel; a test implements it
/// over a vector.
pub trait InventorySink {
    /// The read has reached `step`.
    fn progress(&mut self, step: InventoryStep);

    /// One account, complete.
    fn account(&mut self, account: SourceAccountRecord);

    /// Something that could not be read or did not validate.
    fn warning(&mut self, warning: SourceWarningRecord);

    /// Whether anybody still listens. The read asks between steps and ends
    /// quietly when this is `false`: nothing on either host changes, so
    /// stopping is the whole cancellation. The default says `true`.
    fn is_open(&self) -> bool {
        true
    }
}
