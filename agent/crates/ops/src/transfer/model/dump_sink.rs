//! Where a database transfer reports its steps.

use crate::transfer::model::dump_step::DumpStep;

/// The receiving end of a database transfer's progress.
pub trait DumpSink {
    /// Reports the step the transfer has reached.
    fn step(&mut self, step: DumpStep);
}
