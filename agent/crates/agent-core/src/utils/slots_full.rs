//! The refusal of a bounded count with no slot left.

/// A [`BoundedSlots`](super::bounded_slots::BoundedSlots) had no slot to give: the
/// account already held its share, or the host had none left.
///
/// It carries nothing, and on purpose: which of the two counts refused would tell
/// a customer how busy their neighbours are. Each area maps it onto its own
/// "capacity reached" error.
#[derive(Debug, Clone, Copy, PartialEq, Eq, thiserror::Error)]
#[error("no slot is free")]
pub struct SlotsFull;
