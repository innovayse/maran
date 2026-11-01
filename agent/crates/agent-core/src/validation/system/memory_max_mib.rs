//! The most memory, in MiB, an account's slice may hold.

use super::memory_max_mib_error::MemoryMaxMibError;

/// The largest ceiling accepted, in MiB: one pebibyte. Far above any host; it exists so an extra
/// digit is refused here instead of being handed to systemd as a nonsense figure.
pub const MAXIMUM: u32 = 1_073_741_824;

/// The most memory, in MiB, an account's slice may hold.
///
/// It becomes systemd's `MemoryMax=` on the account's `user-<uid>.slice`, a hard
/// ceiling the kernel enforces through cgroup v2 `memory.max`: past it the
/// slice's own processes are reclaimed from and, failing that, killed by the
/// OOM killer — the account's processes, never another tenant's.
///
/// The inner value is private and [`MemoryMaxMib::parse`] is the only constructor, so
/// holding one is proof the figure is in `1..={MAXIMUM}`. Zero is refused
/// rather than read as "no limit": an absent wire field decodes to zero, and
/// "no limit" is a separate, typed answer the caller states on purpose (the
/// accounts area's `ResourceCeiling::Unlimited`), never a number.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct MemoryMaxMib(u32);

impl MemoryMaxMib {
    /// Validates `candidate` as a memory ceiling and wraps it.
    ///
    /// # Errors
    ///
    /// - [`MemoryMaxMibError::Zero`] when `candidate` is `0`, which is what an absent
    ///   field decodes to and which would otherwise read as a ceiling of nothing.
    /// - [`MemoryMaxMibError::TooLarge`] when `candidate` is above [`MAXIMUM`].
    pub fn parse(candidate: u32) -> Result<Self, MemoryMaxMibError> {
        if candidate == 0 {
            return Err(MemoryMaxMibError::Zero);
        }

        if candidate > MAXIMUM {
            return Err(MemoryMaxMibError::TooLarge {
                value: candidate,
                maximum: MAXIMUM,
            });
        }

        Ok(Self(candidate))
    }

    /// The validated figure.
    #[must_use]
    pub fn value(&self) -> u32 {
        self.0
    }

    /// The `MemoryMax=` assignment `systemctl set-property` is handed for this
    /// figure.
    ///
    /// Built from a validated integer and a fixed key, so the text can carry
    /// nothing but digits after the `=`: no newline, no second assignment, no
    /// option (rules/security.md item 4).
    #[must_use]
    pub fn as_property(&self) -> String {
        format!("MemoryMax={}M", self.0)
    }
}

#[cfg(test)]
#[path = "../../tests/validation/system/memory_max_mib_tests.rs"]
mod tests;
