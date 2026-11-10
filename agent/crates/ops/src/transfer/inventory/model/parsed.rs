//! What a parser hands back: the entries it kept and the warnings it raised.

use super::source_warning_record::SourceWarningRecord;

/// The result of reading one command's output.
///
/// A parser never fails: whatever it cannot read becomes a warning and the rest
/// is still returned, because a hostile or broken entry must cost the entry and
/// not the inventory.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Parsed<T> {
    /// The entries that were read and validated, in the order they appeared.
    pub items: Vec<T>,
    /// What was skipped or cut, in the order it was met.
    pub warnings: Vec<SourceWarningRecord>,
}

impl<T> Parsed<T> {
    /// An empty result.
    #[must_use]
    pub fn new() -> Self {
        Self {
            items: Vec::new(),
            warnings: Vec::new(),
        }
    }
}

impl<T> Default for Parsed<T> {
    /// The same as [`Parsed::new`].
    fn default() -> Self {
        Self::new()
    }
}
