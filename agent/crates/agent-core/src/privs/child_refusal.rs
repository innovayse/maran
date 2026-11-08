//! The one number a child hands to the fork when its work was refused.

/// Largest refusal number a caller may use; `0` means success and is never a refusal.
pub const MAXIMUM_REFUSAL_CODE: u8 = 63;

/// A refusal as a child states it: one byte the caller chose from its own closed list.
///
/// The fork does not know what the numbers mean. It passes the byte to the parent only when it
/// lies in `1..=63`; any other value (including `0`) reaches the parent as a plain
/// [`PrivError::WorkFailed`](super::priv_error::PrivError::WorkFailed), so a confused child
/// cannot forge success or smuggle a wider value.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ChildRefusal {
    /// The caller-chosen number.
    code: u8,
}

impl ChildRefusal {
    /// A refusal carrying `code`.
    #[must_use]
    pub const fn new(code: u8) -> Self {
        Self { code }
    }

    /// The number as stated, in range or not.
    #[must_use]
    pub const fn code(self) -> u8 {
        self.code
    }
}
