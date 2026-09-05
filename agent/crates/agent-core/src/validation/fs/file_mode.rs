//! The permission bits the agent is willing to give a customer's file.

use super::file_mode_error::FileModeError;

/// Every bit outside the nine plain permission bits: setuid, setgid, sticky,
/// and the file-type bits a caller has no business sending at all.
const NOT_PERMISSION_BITS: u32 = !0o777;

/// A file mode that is a plain permission mode, by construction.
///
/// Constructed only by [`FileMode::parse`], so holding one is proof that the
/// bits are `rwxrwxrwx` and nothing else — the same "valid by construction"
/// shape as [`AccountName`](crate::validation::system::name::AccountName) and
/// [`RelativePath`](super::relative_path::RelativePath). Downstream code does
/// not re-check it, because it cannot be built from anything else (rules/rust.md
/// "Validation first").
///
/// A newtype and not a `u32` with an `if` at each layer, for a reason this
/// change learned the hard way: the mode arrives from the panel as a number, and
/// two hand-written refusals in two layers left the EDGES of the validation —
/// which wire code it produces, and whether a layer refuses or quietly masks —
/// untested and independently mutable. A type has one answer and one place to
/// test it.
///
/// `setuid` is the bit that matters. `0o4755` on a file the agent has just
/// created inside a customer's home is a setuid binary that customer owns,
/// written by a root daemon on request.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct FileMode(u32);

impl FileMode {
    /// Accepts `bits` as a plain permission mode.
    ///
    /// # Errors
    ///
    /// Returns [`FileModeError::NotAPlainPermissionMode`] when any bit outside
    /// `0o777` is set.
    pub fn parse(bits: u32) -> Result<Self, FileModeError> {
        if bits & NOT_PERMISSION_BITS != 0 {
            return Err(FileModeError::NotAPlainPermissionMode);
        }

        Ok(Self(bits))
    }

    /// The permission bits, for a `chmod`-shaped call.
    #[must_use]
    pub fn bits(self) -> u32 {
        self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/fs/file_mode_tests.rs"]
mod tests;
